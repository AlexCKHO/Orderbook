namespace Trading.Oms.Api.Oms.Infrastructure.Persistence.Entities;

public class IdempotencyRecordEntity
{
    public string Scope {get; set;}
    public string AccountId  {get; set;}
    public string IdempotencyKey {get; set;}
    public string RequestId {get; set;}
    public string RequestHash {get; set;}
    public string State {get; set;}
    public string ResponseStatusCode {get; set;}
    public DateTimeOffset CreatedAtUtc {get; set;}
    public DateTimeOffset CompletedAtUtc {get; set;}
    public DateTimeOffset ExpiresAtUtc {get; set;}

    

    
    
}