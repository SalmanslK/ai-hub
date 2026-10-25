namespace App.Capture.Contracts;

// One use of an AI tool by an employee, as a connector reports it. Immutable once made;
// Payload is the tool's own detail, as JSON.
public sealed record UsageEvent(string Tool, string Employee, DateTimeOffset OccurredAt, string Kind, string Payload);
