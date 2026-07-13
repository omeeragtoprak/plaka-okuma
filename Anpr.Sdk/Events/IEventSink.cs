namespace Anpr.Sdk.Events;

/// <summary>
/// Plaka olaylarının gönderileceği hedef soyutlaması.
/// Mevcut: ConsoleEventSink, InMemoryEventStore, WebhookEventSink.
/// İleride: DbSink (veritabanına yazma) bu arayüzü uygulayarak eklenebilir.
/// </summary>
public interface IEventSink
{
    Task PublishAsync(PlateEvent plateEvent, CancellationToken cancellationToken);
}
