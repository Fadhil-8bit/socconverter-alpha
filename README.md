# SOCConvertor

A simple ASP.NET Core MVC tool for splitting PDF files by SOA, Invoice, or Debtor Code. Designed for internal admin use.

## Features
- Upload a PDF and split by:
  - Statement of Account (SOA)
  - Invoice
  - Debtor Code
- Preview PDF pages before splitting
- Download all split files as a ZIP
- No authentication required (internal use)


## Screenshots
### Main Upload Interface
![Main Interface](<img width="2339" height="1653" alt="Upload PDF - socconvertor-1" src="https://github.com/user-attachments/assets/e38101b5-e856-448b-9d58-e6c0dc474515" />)

## Getting Started

### Prerequisites
- .NET 9.0 SDK
- Git
- (Optional) Docker

### Running Locally
```powershell
cd "C:\Users\it support\Downloads\socconvertor\socconvertor\socconvertor"
dotnet run
```
Open your browser to the address shown in the console (usually `https://localhost:5001` or similar).

### Docker Build & Run
```powershell
docker build -t socconvertor .
docker run -p 5001:5001 -v ${PWD}/wwwroot/Temp:/app/wwwroot/Temp socconvertor
```

### Usage
1. Go to the web interface
2. Upload your PDF
3. Select split type (SOA, Invoice, Debtor Code)
4. Preview pages if needed
5. Click 'Split' to process
6. Download split files as ZIP

## Notes
- All split files are saved in `wwwroot/Temp`.
- Temp files are overwritten on each upload.
- For multiple users, run separate containers or add per-upload folders.

## License
Internal use only. Not for public distribution.
