namespace App.Processing.Contracts;

// Where Capture's worker hands each stored event. Idempotent per event id: an event handed over
// again is recognised and its effect is not applied a second time.
public interface IUsageEventProcessor
{
    Task ProcessAsync(CapturedEvent capturedEvent, CancellationToken cancellationToken);
}
