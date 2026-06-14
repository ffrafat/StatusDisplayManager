const usb = require('usb');

const VID = 0x1908;
const PID = 0x0102;
const EP_OUT = 0x01;
const EP_IN = 0x81;

const NATIVE_WIDTH = 480;
const NATIVE_HEIGHT = 320;

const DIR_OUT = 0x00;
const DIR_IN = 0x80;

/**
 * Converts a flat RGBA buffer (4 bytes per pixel) to RGB565 Big-Endian buffer (2 bytes per pixel)
 * @param {Buffer|Uint8Array} rgbaBuf 
 * @returns {Buffer}
 */
function toRGB565BE(rgbaBuf) {
  const pixelCount = rgbaBuf.length / 4;
  const outBuf = Buffer.alloc(pixelCount * 2);
  for (let i = 0; i < pixelCount; i++) {
    const r = rgbaBuf[i * 4];
    const g = rgbaBuf[i * 4 + 1];
    const b = rgbaBuf[i * 4 + 2];
    
    // Pack to R5G6B5 uint16
    const rgb565 = ((r & 0xF8) << 8) | ((g & 0xFC) << 3) | (b >> 3);
    
    // Big-endian: High byte first
    outBuf[i * 2] = (rgb565 >> 8) & 0xFF;
    outBuf[i * 2 + 1] = rgb565 & 0xFF;
  }
  return outBuf;
}

class AX206Display {
  constructor(width = NATIVE_WIDTH, height = NATIVE_HEIGHT) {
    this.width = width;
    this.height = height;
    this.device = null;
    this.iface = null;
    this.outEp = null;
    this.inEp = null;
    this.connected = false;
  }

  // Helper to wrap bulk endpoint transfer in a Promise
  _transfer(endpoint, buffer, timeout = 4000) {
    return new Promise((resolve, reject) => {
      endpoint.timeout = timeout;
      endpoint.transfer(buffer, (err) => {
        if (err) reject(err);
        else resolve();
      });
    });
  }

  // Helper to wrap bulk read in a Promise
  _read(endpoint, length, timeout = 4000) {
    return new Promise((resolve, reject) => {
      endpoint.timeout = timeout;
      endpoint.transfer(length, (err, data) => {
        if (err) reject(err);
        else resolve(data);
      });
    });
  }

  open() {
    this.device = usb.findByIds(VID, PID);
    if (!this.device) {
      throw new Error(`AX206 display ${VID.toString(16)}:${PID.toString(16)} not found`);
    }

    this.device.open();
    
    // Select first interface
    this.iface = this.device.interfaces[0];
    
    // Detach kernel driver (required on Linux/Raspberry Pi)
    try {
      if (this.iface.isKernelDriverActive()) {
        this.iface.detachKernelDriver();
      }
    } catch (e) {
      // Ignored if not implemented or fails
    }

    try {
      this.device.setConfiguration(1);
    } catch (e) {
      // Ignored if device is already configured
    }

    this.iface.claim();

    // Find our specific endpoints
    this.outEp = this.iface.endpoints.find(ep => ep.direction === 'out' && ep.address === EP_OUT);
    this.inEp = this.iface.endpoints.find(ep => ep.direction === 'in' && ep.address === EP_IN);

    if (!this.outEp || !this.inEp) {
      throw new Error("Failed to find endpoints 0x01 or 0x81");
    }

    // Clear stalls
    try {
      this.outEp.clearHalt((err) => {});
      this.inEp.clearHalt((err) => {});
    } catch (e) {}

    this.connected = true;
    return this;
  }

  close() {
    if (this.device) {
      try {
        if (this.iface) {
          this.iface.release(true, (err) => {});
        }
      } catch (e) {}
      try {
        this.device.close();
      } catch (e) {}
      this.device = null;
      this.iface = null;
      this.outEp = null;
      this.inEp = null;
      this.connected = false;
    }
  }

  async recover() {
    if (!this.device) return;
    console.log("Executing BOT USB Reset...");
    // Bulk-Only Mass Storage Reset (class request 0xFF)
    try {
      await new Promise((resolve, reject) => {
        this.device.controlTransfer(0x21, 0xFF, 0x0000, 0x0000, Buffer.alloc(0), (err, data) => {
          if (err) reject(err);
          else resolve(data);
        });
      });
    } catch (e) {
      console.warn("BOT Reset warning:", e.message);
    }

    // Clear halt on endpoints
    try {
      await new Promise(res => this.outEp.clearHalt(() => res()));
      await new Promise(res => this.inEp.clearHalt(() => res()));
    } catch (e) {}

    // Drain IN endpoint
    for (let i = 0; i < 3; i++) {
      try {
        await this._read(this.inEp, 64, 60);
      } catch (e) {
        break;
      }
    }
  }

  async reopen() {
    this.close();
    await new Promise(resolve => setTimeout(resolve, 4000));
    try {
      this.open();
      return true;
    } catch (e) {
      return false;
    }
  }

  _cbw(dataLen, direction, cdb) {
    const cbw = Buffer.alloc(31);
    cbw.write("USBC", 0);
    cbw.writeUInt32LE(0xefbeadde, 4); // Tag: 0xdeadbeef
    cbw.writeUInt32LE(dataLen, 8);
    cbw[12] = direction;
    cbw[13] = 0x00; // LUN
    cbw[14] = 0x10; // CDB Length (16)
    cdb.copy(cbw, 15);
    return cbw;
  }

  async _readCsw(retries = 5) {
    let lastError = null;
    for (let i = 0; i < retries; i++) {
      try {
        const csw = await this._read(this.inEp, 13, 2000);
        if (csw.length >= 13 && csw.toString('ascii', 0, 4) === 'USBS') {
          return csw[12]; // Return status byte (0 = OK)
        }
        lastError = new Error(`Bad CSW signature: ${csw.toString('hex')}`);
      } catch (e) {
        lastError = e;
      }
    }
    throw lastError || new Error("Failed to read CSW");
  }

  async _command(cdb, direction = DIR_OUT, data = Buffer.alloc(0)) {
    const blockLen = data.length;
    await this._transfer(this.outEp, this._cbw(blockLen, direction, cdb));
    if (direction === DIR_OUT && blockLen > 0) {
      await this._transfer(this.outEp, data);
    }
    const status = await this._readCsw();
    if (status !== 0) {
      throw new Error(`Command CSW status failed: ${status}`);
    }
  }

  /**
   * Send a rectangle (x0,y0)..(x1,y1) exclusive.
   * @param {number} x0 
   * @param {number} y0 
   * @param {number} x1 
   * @param {number} y1 
   * @param {Buffer} rgb565Buf - Big-Endian RGB565 bytes
   */
  async blit(x0, y0, x1, y1, rgb565Buf) {
    const w = x1 - x0;
    const h = y1 - y0;
    if (rgb565Buf.length !== w * h * 2) {
      throw new Error(`Need ${w * h * 2} bytes, got ${rgb565Buf.length}`);
    }

    const cdb = Buffer.alloc(16);
    cdb[0] = 0xCD;
    cdb[5] = 0x06;
    cdb[6] = 0x12; // BLIT command
    cdb.writeUInt16LE(x0, 7);
    cdb.writeUInt16LE(y0, 9);
    cdb.writeUInt16LE(x1 - 1, 11);
    cdb.writeUInt16LE(y1 - 1, 13);

    await this._command(cdb, DIR_OUT, rgb565Buf);
  }

  /**
   * Blits a raw RGBA buffer directly to the screen by converting it to RGB565 BE.
   * @param {Buffer|Uint8Array} rgbaBuf 
   */
  async drawRGBA(rgbaBuf) {
    const rgb565Buf = toRGB565BE(rgbaBuf);
    await this.blit(0, 0, this.width, this.height, rgb565Buf);
  }
}

module.exports = {
  AX206Display,
  toRGB565BE
};
