// Today's and tomorrow's events from the iPhone's Calendar for the Workspace's calendar widget
// (Widgets.cs, CalendarWidget). Read only. Results go to the GameObject "CalendarNative" as JSON:
// {"events":[{"title","start":"HH:mm","allDay","tomorrow"}]} or {"error":"..."}.
// Info.plist: NSCalendarsFullAccessUsageDescription / NSCalendarsUsageDescription (IosSpeech.cs adds them).

#import <Foundation/Foundation.h>
#import <EventKit/EventKit.h>

extern "C" void UnitySendMessage(const char* obj, const char* method, const char* msg);

static EKEventStore* s_Store;

static void SendJson(NSDictionary* payload)
{
    NSData* json = [NSJSONSerialization dataWithJSONObject:payload options:0 error:nil];
    NSString* text = json ? [[NSString alloc] initWithData:json encoding:NSUTF8StringEncoding] : @"{\"error\":\"JSON\"}";
    dispatch_async(dispatch_get_main_queue(), ^{
        UnitySendMessage("CalendarNative", "OnCalendar", text.UTF8String);
    });
}

static void FetchEvents()
{
    NSCalendar* calendar = NSCalendar.currentCalendar;
    NSDate* start = [calendar startOfDayForDate:NSDate.date];
    NSDate* tomorrow = [calendar dateByAddingUnit:NSCalendarUnitDay value:1 toDate:start options:0];
    NSDate* end = [calendar dateByAddingUnit:NSCalendarUnitDay value:2 toDate:start options:0];
    NSPredicate* predicate = [s_Store predicateForEventsWithStartDate:start endDate:end calendars:nil];
    NSArray<EKEvent*>* events = [[s_Store eventsMatchingPredicate:predicate]
        sortedArrayUsingSelector:@selector(compareStartDateWithEvent:)];
    NSDateFormatter* time = [[NSDateFormatter alloc] init];
    time.dateFormat = @"HH:mm";
    NSMutableArray* items = [NSMutableArray array];
    for (EKEvent* event in events)
    {
        if (items.count >= 12)
            break;
        [items addObject:@{
            @"title": event.title ?: @"",
            @"start": [time stringFromDate:event.startDate] ?: @"",
            @"allDay": @(event.allDay),
            @"tomorrow": @([event.startDate compare:tomorrow] != NSOrderedAscending),
        }];
    }
    SendJson(@{ @"events": items });
}

extern "C" void CardboardXR_CalendarFetch()
{
    if (s_Store == nil)
        s_Store = [[EKEventStore alloc] init];
    void (^done)(BOOL, NSError*) = ^(BOOL granted, NSError* error) {
        if (granted)
            FetchEvents();
        else
            SendJson(@{ @"error": @"Brak dostępu do kalendarza (Ustawienia > Prywatność > Kalendarze)." });
    };
    if (@available(iOS 17.0, *))
        [s_Store requestFullAccessToEventsWithCompletion:done];
    else
        [s_Store requestAccessToEntityType:EKEntityTypeEvent completion:done];
}
