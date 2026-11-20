//
//  APAdvancedPanel.mm
//  AdvancedPanel — Objective-C wrapper over the C ABI.
//
//  Copyright (c) 2026 Sotos. All rights reserved. See LICENSE.
//
//  This file compiles with or without ARC: the CMake target does not pass
//  -fobjc-arc, so the ARC-only constructs (__weak, __bridge) are behind
//  __has_feature(objc_arc) guards.
//

#import "APAdvancedPanel.h"
#include "advanced_panel.h"

static NSString *APStringOrEmpty(const char *s) {
    return s ? [NSString stringWithUTF8String:s] : @"";
}

// __bridge is an ARC keyword; under manual retain/release the cast is plain.
#if __has_feature(objc_arc)
#  define AP_BRIDGE_TO_OBJECT(x) ((__bridge id)(x))
#  define AP_BRIDGE_TO_VOID(x)   ((__bridge void *)(x))
#else
#  define AP_BRIDGE_TO_OBJECT(x) ((id)(x))
#  define AP_BRIDGE_TO_VOID(x)   ((void *)(x))
#endif

// The handler ivar is @private, so the C callback below cannot read it
// directly. It calls this method instead.
@interface APAdvancedPanel ()
- (void)ap_deliverEventTab:(const char *)tabID
                    widget:(const char *)widgetID
                      kind:(NSInteger)kind
                     value:(double)value;
@end

static void APEventTrampoline(void *user,
                              const char *tabID,
                              const char *widgetID,
                              APEventKind kind,
                              double value) {
    APAdvancedPanel *panel = AP_BRIDGE_TO_OBJECT(user);
    if (!panel) return;
    [panel ap_deliverEventTab:tabID widget:widgetID kind:(NSInteger)kind value:value];
}

@implementation APAdvancedPanel {
    APContext    *_ctx;
    APEventBlock _handler;
}

- (nullable instancetype)initWithTitle:(NSString *)title
                               version:(NSString *)version
                                author:(NSString *)author
                                 width:(NSInteger)width
                                height:(NSInteger)height
                                 theme:(APTheme)theme {
    self = [super init];
    if (!self) return nil;

    APPanelConfig cfg;
    cfg.title   = title.UTF8String;
    cfg.version = version.UTF8String;
    cfg.author  = author.UTF8String;
    cfg.width   = (int)width;
    cfg.height  = (int)height;
    cfg.theme   = (theme == AP_THEME_LIGHT) ? AP_THEME_LIGHT : AP_THEME_DARK;
    cfg.flags   = 0;

    _ctx = ap_create(&cfg);
    if (!_ctx) return nil;

    // The context only borrows `self`; it never retains it, and it is
    // destroyed in -dealloc before self goes away.
    ap_set_callback(_ctx, APEventTrampoline, AP_BRIDGE_TO_VOID(self));

    _title   = [title copy];
    _version = [version copy];
    return self;
}

- (void)dealloc {
    if (_ctx) {
        ap_set_callback(_ctx, NULL, NULL);
        ap_destroy(_ctx);
        _ctx = NULL;
    }
#if !__has_feature(objc_arc)
    [super dealloc];
#endif
}

- (void)ap_deliverEventTab:(const char *)tabID
                    widget:(const char *)widgetID
                      kind:(NSInteger)kind
                     value:(double)value {
    APEventBlock block = _handler;
    if (!block) return;
    block(APStringOrEmpty(tabID), APStringOrEmpty(widgetID), kind, value);
}

- (NSInteger)tabCount { return _ctx ? ap_tab_count(_ctx) : 0; }

- (BOOL)addTab:(NSString *)tabID title:(NSString *)tabTitle {
    return _ctx && ap_add_tab(_ctx, tabID.UTF8String, tabTitle.UTF8String) == AP_OK;
}

- (BOOL)removeTab:(NSString *)tabID {
    return _ctx && ap_remove_tab(_ctx, tabID.UTF8String) == AP_OK;
}

- (BOOL)addToggleInTab:(NSString *)tabID
                widget:(NSString *)widgetID
                 label:(NSString *)label
               initial:(BOOL)initial {
    return _ctx && ap_add_toggle(_ctx, tabID.UTF8String, widgetID.UTF8String,
                                 label.UTF8String, initial ? 1 : 0) == AP_OK;
}

- (BOOL)addSliderInTab:(NSString *)tabID
                widget:(NSString *)widgetID
                 label:(NSString *)label
                   min:(double)minValue
                   max:(double)maxValue
               initial:(double)initial {
    return _ctx && ap_add_slider(_ctx, tabID.UTF8String, widgetID.UTF8String,
                                 label.UTF8String, minValue, maxValue, initial) == AP_OK;
}

- (BOOL)addButtonInTab:(NSString *)tabID
                widget:(NSString *)widgetID
                 label:(NSString *)label {
    return _ctx && ap_add_button(_ctx, tabID.UTF8String, widgetID.UTF8String,
                                 label.UTF8String) == AP_OK;
}

- (BOOL)addLabelInTab:(NSString *)tabID
               widget:(NSString *)widgetID
                 text:(NSString *)text {
    return _ctx && ap_add_label(_ctx, tabID.UTF8String, widgetID.UTF8String,
                                text.UTF8String) == AP_OK;
}

- (BOOL)toggleValueForWidget:(NSString *)widgetID {
    return _ctx && ap_get_toggle(_ctx, widgetID.UTF8String) == 1;
}

- (void)setToggleValue:(BOOL)value forWidget:(NSString *)widgetID {
    if (_ctx) ap_set_toggle(_ctx, widgetID.UTF8String, value ? 1 : 0);
}

- (double)sliderValueForWidget:(NSString *)widgetID {
    return _ctx ? ap_get_slider(_ctx, widgetID.UTF8String) : 0.0;
}

- (BOOL)setSliderValue:(double)value forWidget:(NSString *)widgetID {
    return _ctx && ap_set_slider(_ctx, widgetID.UTF8String, value) == AP_OK;
}

- (void)setEventHandler:(nullable APEventBlock)handler {
    _handler = [handler copy];
}

- (BOOL)dispatchClickInTab:(NSString *)tabID widget:(NSString *)widgetID {
    return _ctx && ap_dispatch_event(_ctx, tabID.UTF8String, widgetID.UTF8String,
                                     AP_EVENT_CLICK, 0.0) == AP_OK;
}

- (BOOL)dispatchSliderInTab:(NSString *)tabID widget:(NSString *)widgetID value:(double)value {
    return _ctx && ap_dispatch_event(_ctx, tabID.UTF8String, widgetID.UTF8String,
                                     AP_EVENT_VALUE_CHANGED, value) == AP_OK;
}

- (nullable NSString *)JSONRepresentation {
    if (!_ctx) return nil;
    const char *json = ap_serialize(_ctx);
    if (!json) return nil;
    NSString *result = [NSString stringWithUTF8String:json];
    ap_free_string(json);
    return result;
}

@end
