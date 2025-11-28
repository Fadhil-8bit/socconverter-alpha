namespace socconvertor.Services;

public interface IContactProvider
{
    /// <summary>
    /// Returns a preferred email for a debtor code, or null if none.
    /// </summary>
    string? GetEmailForDebtor(string debtorCode);

    /// <summary>
    /// Returns full contact details (debtor code, company, notes, routed emails) if available.
    /// </summary>
    GoogleContactEntry? GetDetails(string debtorCode);
}
