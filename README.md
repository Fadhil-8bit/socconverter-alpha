# SOCConvertor

Internal tool with two distinct workflows:

## Workflow A: Split PDFs
1. Navigate to "Split PDFs".
2. Upload a single master PDF.
3. Choose split type (SOA / Invoice / Overdue).
4. (Optional) Preview pages.
5. Split executes; result list displays generated PDFs.
6. Download individual files or the combined ZIP.

## Workflow B: Bulk Email
1. Navigate to "Bulk Upload ZIPs".
2. Upload one or more ZIPs (soa.zip / invoice.zip / od.zip) + optional custom code.
3. System creates a single session folder with extracted PDFs (origin=ZIP).
4. Navigate to "Bulk Email Sessions" to select one or more session folders.
5. Click Scan/Group to build an email session.
6. Preview debtor groups and adjust email addresses.
7. Send bulk emails; view result summary.

## Features
- PDF splitting by SOA / Invoice / Overdue with per-upload isolated folders.
- ZIP ingestion for pre-split PDFs; origin tagging.
- Unknown type handling (files without tokens remain unchanged and marked UNKNOWN).
- Distinct navigation for Splitting vs Bulk Email.

## Getting Started

### Prerequisites
- .NET 9.0 SDK
- Git

### Run
```powershell
dotnet run
```
Browse to the URL shown in console.

### Docker
```powershell
docker build -t socconvertor .
docker run -p 5001:5001 -v ${PWD}/wwwroot/Temp:/app/wwwroot/Temp socconvertor
```

## Notes
- Session folders live under `wwwroot/Temp`.
- Origin markers: `.origin.split` or `.origin.zip`.
- TempData only used for user messages; workflow state passed via route/query.

## License
Internal use only.
