// Declarations for the private libIOReport.dylib (ships with macOS, tbd in the SDK).
// The same calls powermetrics uses for its Energy Model numbers; no root needed.
#pragma once
#include <CoreFoundation/CoreFoundation.h>
#include <stdint.h>

typedef struct IOReportSubscriptionRef *IOReportSubscriptionRef;

CFDictionaryRef _Nullable IOReportCopyChannelsInGroup(CFStringRef _Nullable group, CFStringRef _Nullable subgroup,
                                                      uint64_t a, uint64_t b, uint64_t c) CF_RETURNS_RETAINED;
IOReportSubscriptionRef _Nullable IOReportCreateSubscription(void *_Nullable a, CFMutableDictionaryRef _Nonnull desired,
                                                             CFMutableDictionaryRef _Nullable *_Nonnull subbed,
                                                             uint64_t channelID, CFTypeRef _Nullable b);
CFDictionaryRef _Nullable IOReportCreateSamples(IOReportSubscriptionRef _Nonnull sub, CFMutableDictionaryRef _Nonnull subbed,
                                                CFTypeRef _Nullable a) CF_RETURNS_RETAINED;
CFDictionaryRef _Nullable IOReportCreateSamplesDelta(CFDictionaryRef _Nonnull prev, CFDictionaryRef _Nonnull cur,
                                                     CFTypeRef _Nullable a) CF_RETURNS_RETAINED;
CFStringRef _Nullable IOReportChannelGetGroup(CFDictionaryRef _Nonnull channel) CF_RETURNS_NOT_RETAINED;
CFStringRef _Nullable IOReportChannelGetSubGroup(CFDictionaryRef _Nonnull channel) CF_RETURNS_NOT_RETAINED;
CFStringRef _Nullable IOReportChannelGetChannelName(CFDictionaryRef _Nonnull channel) CF_RETURNS_NOT_RETAINED;
CFStringRef _Nullable IOReportChannelGetUnitLabel(CFDictionaryRef _Nonnull channel) CF_RETURNS_NOT_RETAINED;
int64_t IOReportSimpleGetIntegerValue(CFDictionaryRef _Nonnull channel, int32_t index);
