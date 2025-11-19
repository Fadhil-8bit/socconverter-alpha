namespace PdfReaderDemo.Services;

public interface IContactProvider
{
    /// <summary>
    /// Returns a preferred email for a debtor code, or null if none.
    /// </summary>
    string? GetEmailForDebtor(string debtorCode);
}
