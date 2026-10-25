namespace App.Processing.Contracts;

// A usage event as Capture stored it, with the id Capture gave it.
public sealed record CapturedEvent(Guid Id, string Tool, string Employee, DateTimeOffset OccurredAt, string Kind, string Payload);
