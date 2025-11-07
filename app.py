import os
import re
import io
import zipfile
import uuid
import shutil
from flask import Flask, request, send_file, render_template_string, jsonify, send_from_directory
from pypdf import PdfReader, PdfWriter
import threading
from datetime import datetime

# --- Configuration (No Changes) ---
app = Flask(__name__)
app.config['HOST'] = '0.0.0.0'
app.config['PORT'] = 5000
UPLOAD_FOLDER = 'uploads'
PROCESSED_FOLDER = 'processed'
ALLOWED_EXTENSIONS = {'pdf'}
CLEANUP_DELAY = 3600 
os.makedirs(UPLOAD_FOLDER, exist_ok=True)
os.makedirs(PROCESSED_FOLDER, exist_ok=True)

# --- REGEX PATTERN (No Changes) ---
DEBTOR_CODE_PATTERN = re.compile(r'Debtor Code\s*:\s*([A-Z0-9]+-[A-Z0-9]+)', re.IGNORECASE)

# --- Backend Logic (All Unchanged) ---
def cleanup_folder(folder_path):
    print(f"SCHEDULING CLEANUP for folder: {folder_path} in {CLEANUP_DELAY} seconds.")
    try:
        shutil.rmtree(folder_path)
        print(f"SUCCESS: Automatically cleaned up folder: {folder_path}")
    except FileNotFoundError:
        print(f"INFO: Cleanup skipped, folder not found (already deleted): {folder_path}")
    except Exception as e:
        print(f"ERROR: Failed to cleanup folder {folder_path}: {e}")
def allowed_file(filename):
    return '.' in filename and filename.rsplit('.', 1)[1].lower() in ALLOWED_EXTENSIONS
def split_pdf_and_save(pdf_file_stream, custom_code, session_id):
    session_dir = os.path.join(PROCESSED_FOLDER, session_id)
    os.makedirs(session_dir, exist_ok=True)
    processed_files = []
    writers = {}
    current_debtor_code = "UNCLASSIFIED"
    reader = PdfReader(pdf_file_stream)
    for page in reader.pages:
        page_text = page.extract_text() or ""
        debtor_match = DEBTOR_CODE_PATTERN.search(page_text)
        if debtor_match:
            new_debtor_code = debtor_match.group(1).strip()
            if new_debtor_code != current_debtor_code:
                current_debtor_code = new_debtor_code
        if current_debtor_code not in writers:
            writers[current_debtor_code] = PdfWriter()
        writers[current_debtor_code].add_page(page)
    for debtor_code, writer in writers.items():
        safe_custom_code = re.sub(r'[^a-zA-Z0-9\s-]', '', custom_code)
        filename = f"{debtor_code} OD {safe_custom_code}.pdf"
        filepath = os.path.join(session_dir, filename)
        with open(filepath, "wb") as f_out:
            writer.write(f_out)
        processed_files.append({"filename": filename, "debtor_code": debtor_code})
    return processed_files

# --- Flask HTML Template (DEFINITIVE SCROLLBAR FIX) ---
HTML_TEMPLATE = """
<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="UTF-8">
    <meta name="viewport" content="width=device-width, initial-scale=1.0">
    <title>OD PDF Splitter</title>
    <style>
        :root {
            --accent-color: #3d3d3d; --accent-hover: #2b2b2b;
            --secondary-color: #e8e8ed; --secondary-hover: #dcdce1;
            --background-color: #e8e8ed; --surface-color: #ffffff;
            --sidebar-color: #f5f5f7; --text-primary: #1d1d1f;
            --text-secondary: #515154; --border-color: #d2d2d7;
            --font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", "Helvetica Neue", Arial, sans-serif;
        }
        body {
            font-family: var(--font-family); background-color: var(--background-color);
            color: var(--text-primary); margin: 0; display: flex;
            justify-content: center; align-items: center; min-height: 100vh;
        }
        .app-window {
            display: flex; width: 90vw; max-width: 1400px;
            height: 90vh; max-height: 800px; background-color: var(--surface-color);
            border-radius: 12px; border: 1px solid var(--border-color);
            box-shadow: 0 8px 32px rgba(0,0,0,0.1); overflow: hidden;
        }
        .sidebar {
            flex: 0 0 320px; background-color: var(--sidebar-color);
            border-right: 1px solid var(--border-color); padding: 24px;
            display: flex; flex-direction: column;
        }
        .sidebar-header { text-align: center; margin-bottom: 32px; }
        .sidebar h1 { font-size: 20px; font-weight: 600; margin: 0 0 4px 0; }
        .sidebar .subtitle { font-size: 14px; color: var(--text-secondary); }
        .content-area { flex: 1; padding: 32px; display: flex; flex-direction: column; min-height: 0; }
        .placeholder-view {
            flex: 1; display: flex; flex-direction: column; justify-content: center;
            align-items: center; color: var(--text-secondary); text-align: center;
        }
        .placeholder-view svg { margin-bottom: 16px; }
        .results-header { display: flex; justify-content: space-between; align-items: center; margin-bottom: 16px; }
        .results-header h2 { font-size: 18px; font-weight: 600; margin: 0; }
        
        /* --- SCROLLBAR FIX --- */
        .results-view {
            display: none;
            flex: 1; /* Grow to fill content area */
            flex-direction: column;
            min-height: 0; /* Critical for nested flex scrolling */
        }
        .results-list {
            flex: 1; /* Grow to fill remaining space in results-view */
            border: 1px solid var(--border-color);
            border-radius: 8px;
            overflow-y: auto; /* This will now work */
            padding: 8px;
            min-height: 0; /* Also important here */
        }
        /* --- END FIX --- */

        .input-group { margin-bottom: 24px; }
        .input-group label { display: block; font-size: 14px; font-weight: 500; margin-bottom: 8px; }
        .file-upload-box {
            display: flex; align-items: center; padding: 12px; border: 1px solid var(--border-color);
            border-radius: 8px; background-color: #fff; cursor: pointer; transition: background-color 0.2s;
        }
        .file-upload-box:hover { background-color: #f0f0f0; }
        .file-upload-input { display: none; }
        .input-field { width: 100%; padding: 12px; border: 1px solid var(--border-color); border-radius: 8px; font-size: 14px; box-sizing: border-box; }
        .btn {
            display: flex; justify-content: center; align-items: center; gap: 8px;
            width: auto; height: 44px; padding: 0 24px; border: none; border-radius: 8px;
            font-size: 14px; font-weight: 500; cursor: pointer; transition: all 0.2s;
        }
        .btn.full-width { width: 100%; }
        .btn:disabled { background-color: #a0a0a0; cursor: not-allowed; }
        .btn-primary { background-color: var(--accent-color); color: white; }
        .btn-primary:hover:not(:disabled) { background-color: var(--accent-hover); }
        .btn-secondary { background-color: var(--secondary-color); color: var(--text-primary); }
        .btn-secondary:hover:not(:disabled) { background-color: var(--secondary-hover); }
        .sidebar .btn-primary { margin-top: auto; }
        .spinner { display: none; width: 18px; height: 18px; border: 2px solid rgba(255,255,255,0.3);
            border-top-color: #fff; border-radius: 50%; animation: spin 1s linear infinite; }
        .spinner.active { display: block; }
        @keyframes spin { to { transform: rotate(360deg); } }
        .file-item { font-size: 13px; padding: 8px 12px; border-radius: 6px; }
        .file-item:nth-child(odd) { background-color: #f5f5f7; }
        .status-message {
            text-align: center; font-weight: 500; font-size: 13px; padding: 8px;
            border-radius: 6px; display: none; margin-top: 16px;
        }
        .status-success { background-color: #dff0d8; color: #3c763d; display: block; }
        .status-error { background-color: #f2dede; color: #a94442; display: block; }
    </style>
</head>
<body>
    <div class="app-window">
        <aside class="sidebar">
            <div class="sidebar-header">
                <h1>OD PDF Splitter</h1>
                <p class="subtitle">Configuration</p>
            </div>
            <form id="upload-form">
                <div class="input-group">
                    <label for="pdf-file-input">1. Master Document</label>
                    <label for="pdf-file-input" class="file-upload-box">
                        <svg xmlns="http://www.w3.org/2000/svg" width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" style="color:var(--text-secondary); margin-right:12px; flex-shrink:0;"><path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"></path><polyline points="14 2 14 8 20 8"></polyline><line x1="12" y1="18" x2="12" y2="12"></line><line x1="9" y1="15" x2="12" y2="12"></line><line x1="15" y1="15" x2="12" y2="12"></line></svg>
                        <span id="file-name-label" style="white-space: nowrap; overflow: hidden; text-overflow: ellipsis;">Choose a file...</span>
                    </label>
                    <input type="file" id="pdf-file-input" name="file" class="file-upload-input" accept=".pdf" required>
                </div>
                <div class="input-group">
                    <label for="custom-code-input">2. Custom Code</label>
                    <input type="text" id="custom-code-input" name="custom_code" class="input-field" placeholder="e.g., Q4-Report">
                </div>
                <button type="submit" class="btn btn-primary full-width">
                    <span id="button-text">Process & Preview</span>
                    <div class="spinner" id="loader"></div>
                </button>
            </form>
             <div id="status-message" class="status-message"></div>
        </aside>
        <main class="content-area">
            <div class="placeholder-view" id="placeholder-view">
                <svg xmlns="http://www.w3.org/2000/svg" width="64" height="64" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1" stroke-linecap="round" stroke-linejoin="round"><rect x="3" y="3" width="18" height="18" rx="2" ry="2"></rect><line x1="3" y1="9" x2="21" y2="9"></line><line x1="9" y1="21" x2="9" y2="9"></line></svg>
                <h2>Your split files will appear here</h2>
                <p>Complete the steps on the left to get started.</p>
            </div>
            <div class="results-view" id="results-view">
                <div class="results-header">
                    <h2>Generated Files (<span id="file-count-label"></span>)</h2>
                    <div id="action-buttons-container">
                        <button id="download-all-zip" class="btn btn-primary">
                            <span class="btn-text">Download All (.zip)</span>
                            <div class="spinner"></div>
                        </button>
                        <button id="reset-button" class="btn btn-secondary" style="display: none;">
                            Split Next Document
                        </button>
                    </div>
                </div>
                <div class="results-list" id="results-container"></div>
            </div>
        </main>
    </div>

    <script>
        // JAVASCRIPT IS UNCHANGED
        const placeholderView = document.getElementById('placeholder-view');
        const resultsView = document.getElementById('results-view');
        const resultsContainer = document.getElementById('results-container');
        const loader = document.getElementById('loader');
        const buttonText = document.getElementById('button-text');
        const statusMessage = document.getElementById('status-message');
        const downloadButton = document.getElementById('download-all-zip');
        const resetButton = document.getElementById('reset-button');
        const form = document.getElementById('upload-form');
        const fileInput = document.getElementById('pdf-file-input');
        const fileNameLabel = document.getElementById('file-name-label');
        const fileCountLabel = document.getElementById('file-count-label');
        let currentSessionId = '', generatedFiles = [];

        fileInput.addEventListener('change', () => { fileNameLabel.textContent = fileInput.files.length > 0 ? fileInput.files[0].name : 'Choose a file...'; });
        
        function resetUI() {
            placeholderView.style.display = 'flex';
            resultsView.style.display = 'none';
            statusMessage.style.display = 'none';
            form.reset();
            fileNameLabel.textContent = 'Choose a file...';
            currentSessionId = '', generatedFiles = [];
            downloadButton.style.display = 'flex';
            resetButton.style.display = 'none';
            const btnText = downloadButton.querySelector('.btn-text');
            const btnSpinner = downloadButton.querySelector('.spinner');
            downloadButton.disabled = false;
            btnText.textContent = 'Download All (.zip)';
            btnSpinner.classList.remove('active');
        }

        function showStatus(message, type = 'error') {
            statusMessage.textContent = message;
            statusMessage.className = `status-message status-${type}`;
        }

        form.addEventListener('submit', async (e) => {
            e.preventDefault();
            loader.classList.add('active'); buttonText.style.display = 'none'; statusMessage.style.display = 'none';
            try {
                const response = await fetch('/upload', { method: 'POST', body: new FormData(form) });
                if (!response.ok) { throw new Error((await response.json()).error || 'Server error'); }
                const data = await response.json();
                if (data.files.length === 0) { throw new Error('No debtor codes were found in the PDF.'); }
                currentSessionId = data.session_id;
                generatedFiles = data.files.map(f => f.filename);
                displayResults(generatedFiles);
            } catch (error) {
                showStatus(error.message, 'error');
            } finally {
                loader.classList.remove('active'); buttonText.style.display = 'block';
            }
        });

        function displayResults(files) {
            resultsContainer.innerHTML = files.map(filename => `<div class="file-item">${filename}</div>`).join('');
            fileCountLabel.textContent = `${files.length}`;
            placeholderView.style.display = 'none';
            resultsView.style.display = 'flex';
        }

        downloadButton.addEventListener('click', async () => {
            const btnText = downloadButton.querySelector('.btn-text');
            const btnSpinner = downloadButton.querySelector('.spinner');
            downloadButton.disabled = true;
            btnText.textContent = 'Preparing...';
            btnSpinner.classList.add('active');
            try {
                const response = await fetch('/download-zip', {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({ files: generatedFiles, session_id: currentSessionId }),
                });
                if (response.ok) {
                    const blob = await response.blob();
                    const url = window.URL.createObjectURL(blob);
                    const a = Object.assign(document.createElement('a'), { href: url, download: response.headers.get('Content-Disposition').split('filename=')[1].replaceAll('"', '') });
                    document.body.appendChild(a); a.click(); a.remove(); window.URL.revokeObjectURL(url);
                    
                    showStatus('Download successful!', 'success');
                    downloadButton.style.display = 'none';
                    resetButton.style.display = 'flex';
                } else {
                    throw new Error('Failed to download the ZIP file.');
                }
            } catch (error) {
                showStatus(error.message, 'error');
                downloadButton.disabled = false;
                btnText.textContent = 'Download All (.zip)';
                btnSpinner.classList.remove('active');
            }
        });

        resetButton.addEventListener('click', resetUI);
    </script>
</body>
</html>
"""

# --- Flask Routes ---
@app.route('/', methods=['GET'])
def index():
    return render_template_string(HTML_TEMPLATE)
@app.route('/upload', methods=['POST'])
def upload_and_process():
    if 'file' not in request.files: return jsonify({"error": "No file part"}), 400
    file = request.files['file']
    custom_code = request.form.get('custom_code') or 'split'
    if file.filename == '' or not allowed_file(file.filename): return jsonify({"error": "Invalid file"}), 400
    try:
        pdf_stream = io.BytesIO(file.read())
        session_id = str(uuid.uuid4())
        files = split_pdf_and_save(pdf_stream, custom_code, session_id)
        session_dir_path = os.path.join(PROCESSED_FOLDER, session_id)
        cleanup_thread = threading.Timer(CLEANUP_DELAY, cleanup_folder, args=[session_dir_path])
        cleanup_thread.start()
        return jsonify({"files": files, "session_id": session_id})
    except Exception as e:
        print(f"Error during processing: {e}")
        return jsonify({"error": f"An error occurred during PDF processing: {str(e)}"}), 500
@app.route('/processed/<session_id>/<path:filename>')
def download_file(session_id, filename):
    directory = os.path.join(PROCESSED_FOLDER, session_id)
    return send_from_directory(directory, filename, as_attachment=True)
@app.route('/download-zip', methods=['POST'])
def download_zip():
    data = request.json
    filenames, session_id = data.get('files', []), data.get('session_id')
    if not filenames or not session_id: return "Invalid request", 400
    session_dir_path = os.path.join(PROCESSED_FOLDER, session_id)
    zip_stream = io.BytesIO()
    with zipfile.ZipFile(zip_stream, 'w', zipfile.ZIP_DEFLATED) as zf:
        for filename in filenames:
            filepath = os.path.join(session_dir_path, filename)
            if os.path.exists(filepath):
                zf.write(filepath, arcname=filename)
    zip_stream.seek(0)
    shutil.rmtree(session_dir_path, ignore_errors=True)
    date_str = datetime.now().strftime("%y%m%d")
    download_filename = f"OD_Splitted_{date_str}.zip"
    response = send_file(zip_stream, mimetype='application/zip', as_attachment=True, download_name=download_filename)
    response.headers["Access-Control-Expose-Headers"] = "Content-Disposition"
    return response

if __name__ == '__main__':
    app.run(debug=True, host=app.config['HOST'], port=app.config['PORT'])