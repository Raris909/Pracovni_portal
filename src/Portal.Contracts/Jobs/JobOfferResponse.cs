namespace Portal.Contracts.Jobs;

public sealed record JobOfferResponse(Guid Id, string Title, string CompanyName, string? Location, string WorkMode);
