/**
 * SmartStudy Textbook Upload - JavaScript/TypeScript Examples
 * 
 * This file contains example code for uploading textbook PDFs to SmartStudy
 * from various frontend frameworks (React, Vue, vanilla JS, etc.)
 */

// ============================================
// 1. VANILLA JAVASCRIPT / FETCH API
// ============================================

/**
 * Upload a textbook PDF with metadata using vanilla JavaScript
 * @param {string} className - Class identifier (e.g., "Grade-10", "CS101")
 * @param {string} subject - Subject name (e.g., "Mathematics", "Physics")
 * @param {string} chapter - Chapter identifier (e.g., "Chapter-1", "Introduction")
 * @param {File} file - PDF file object
 * @returns {Promise<object>} Upload result
 */
async function uploadTextbook(className, subject, chapter, file) {
    const FUNCTION_URL = 'http://localhost:7071/api/upload/textbook';
    // For production: 'https://your-app.azurewebsites.net/api/upload/textbook?code=YOUR_KEY'

    // Create FormData
    const formData = new FormData();
    formData.append('className', className);
    formData.append('subject', subject);
    formData.append('chapter', chapter);
    formData.append('file', file);

    try {
        const response = await fetch(FUNCTION_URL, {
            method: 'POST',
            body: formData
            // Note: Do NOT set Content-Type header - browser sets it automatically with boundary
        });

        const result = await response.json();

        if (!response.ok) {
            throw new Error(result.message || result.error || 'Upload failed');
        }

        return result;
    } catch (error) {
        console.error('Upload error:', error);
        throw error;
    }
}

// Usage example:
/*
const fileInput = document.getElementById('pdfFile');
const file = fileInput.files[0];

uploadTextbook('Grade-10', 'Mathematics', 'Chapter-1-Algebra', file)
    .then(result => {
        console.log('Upload successful:', result);
        alert(`File uploaded: ${result.data.fileName}`);
    })
    .catch(error => {
        console.error('Upload failed:', error);
        alert(`Upload failed: ${error.message}`);
    });
*/


// ============================================
// 2. REACT COMPONENT EXAMPLE
// ============================================

/*
import React, { useState } from 'react';

function TextbookUpload() {
    const [formData, setFormData] = useState({
        className: '',
        subject: '',
        chapter: ''
    });
    const [file, setFile] = useState(null);
    const [uploading, setUploading] = useState(false);
    const [message, setMessage] = useState('');

    const handleInputChange = (e) => {
        setFormData({
            ...formData,
            [e.target.name]: e.target.value
        });
    };

    const handleFileChange = (e) => {
        setFile(e.target.files[0]);
    };

    const handleSubmit = async (e) => {
        e.preventDefault();
        
        if (!file || !formData.className || !formData.subject || !formData.chapter) {
            setMessage('Please fill in all fields');
            return;
        }

        setUploading(true);
        setMessage('');

        try {
            const data = new FormData();
            data.append('className', formData.className);
            data.append('subject', formData.subject);
            data.append('chapter', formData.chapter);
            data.append('file', file);

            const response = await fetch('http://localhost:7071/api/upload/textbook', {
                method: 'POST',
                body: data
            });

            const result = await response.json();

            if (response.ok && result.success) {
                setMessage(`✓ Upload successful: ${result.data.fileName}`);
                // Reset form
                setFormData({ className: '', subject: '', chapter: '' });
                setFile(null);
            } else {
                setMessage(`✗ Upload failed: ${result.message}`);
            }
        } catch (error) {
            setMessage(`✗ Error: ${error.message}`);
        } finally {
            setUploading(false);
        }
    };

    return (
        <div className="upload-container">
            <h2>Upload Textbook</h2>
            <form onSubmit={handleSubmit}>
                <input
                    type="text"
                    name="className"
                    placeholder="Class Name (e.g., Grade-10)"
                    value={formData.className}
                    onChange={handleInputChange}
                    required
                />
                <input
                    type="text"
                    name="subject"
                    placeholder="Subject (e.g., Mathematics)"
                    value={formData.subject}
                    onChange={handleInputChange}
                    required
                />
                <input
                    type="text"
                    name="chapter"
                    placeholder="Chapter (e.g., Chapter-1)"
                    value={formData.chapter}
                    onChange={handleInputChange}
                    required
                />
                <input
                    type="file"
                    accept=".pdf"
                    onChange={handleFileChange}
                    required
                />
                <button type="submit" disabled={uploading}>
                    {uploading ? 'Uploading...' : 'Upload'}
                </button>
            </form>
            {message && <div className="message">{message}</div>}
        </div>
    );
}

export default TextbookUpload;
*/


// ============================================
// 3. VUE COMPONENT EXAMPLE
// ============================================

/*
<template>
  <div class="upload-container">
    <h2>Upload Textbook</h2>
    <form @submit.prevent="handleUpload">
      <input
        v-model="className"
        type="text"
        placeholder="Class Name"
        required
      />
      <input
        v-model="subject"
        type="text"
        placeholder="Subject"
        required
      />
      <input
        v-model="chapter"
        type="text"
        placeholder="Chapter"
        required
      />
      <input
        type="file"
        accept=".pdf"
        @change="handleFileChange"
        required
      />
      <button type="submit" :disabled="uploading">
        {{ uploading ? 'Uploading...' : 'Upload' }}
      </button>
    </form>
    <div v-if="message" class="message">{{ message }}</div>
  </div>
</template>

<script>
export default {
  data() {
    return {
      className: '',
      subject: '',
      chapter: '',
      file: null,
      uploading: false,
      message: ''
    };
  },
  methods: {
    handleFileChange(event) {
      this.file = event.target.files[0];
    },
    async handleUpload() {
      if (!this.file || !this.className || !this.subject || !this.chapter) {
        this.message = 'Please fill in all fields';
        return;
      }

      this.uploading = true;
      this.message = '';

      const formData = new FormData();
      formData.append('className', this.className);
      formData.append('subject', this.subject);
      formData.append('chapter', this.chapter);
      formData.append('file', this.file);

      try {
        const response = await fetch('http://localhost:7071/api/upload/textbook', {
          method: 'POST',
          body: formData
        });

        const result = await response.json();

        if (response.ok && result.success) {
          this.message = `✓ Upload successful: ${result.data.fileName}`;
          // Reset form
          this.className = '';
          this.subject = '';
          this.chapter = '';
          this.file = null;
        } else {
          this.message = `✗ Upload failed: ${result.message}`;
        }
      } catch (error) {
        this.message = `✗ Error: ${error.message}`;
      } finally {
        this.uploading = false;
      }
    }
  }
};
</script>
*/


// ============================================
// 4. AXIOS EXAMPLE (for React/Vue/etc.)
// ============================================

/*
import axios from 'axios';

async function uploadWithAxios(className, subject, chapter, file) {
    const formData = new FormData();
    formData.append('className', className);
    formData.append('subject', subject);
    formData.append('chapter', chapter);
    formData.append('file', file);

    try {
        const response = await axios.post(
            'http://localhost:7071/api/upload/textbook',
            formData,
            {
                headers: {
                    'Content-Type': 'multipart/form-data'
                },
                onUploadProgress: (progressEvent) => {
                    const percentCompleted = Math.round(
                        (progressEvent.loaded * 100) / progressEvent.total
                    );
                    console.log(`Upload progress: ${percentCompleted}%`);
                }
            }
        );

        return response.data;
    } catch (error) {
        console.error('Upload error:', error.response?.data || error.message);
        throw error;
    }
}
*/


// ============================================
// 5. JQUERY EXAMPLE
// ============================================

/*
function uploadWithJQuery(className, subject, chapter, fileInput) {
    const formData = new FormData();
    formData.append('className', className);
    formData.append('subject', subject);
    formData.append('chapter', chapter);
    formData.append('file', fileInput.files[0]);

    $.ajax({
        url: 'http://localhost:7071/api/upload/textbook',
        type: 'POST',
        data: formData,
        processData: false,
        contentType: false,
        success: function(result) {
            console.log('Upload successful:', result);
            alert(`File uploaded: ${result.data.fileName}`);
        },
        error: function(xhr) {
            const error = xhr.responseJSON;
            console.error('Upload failed:', error);
            alert(`Upload failed: ${error.message || error.error}`);
        }
    });
}
*/


// ============================================
// 6. TYPESCRIPT INTERFACE DEFINITIONS
// ============================================

/*
// Request types
interface UploadTextbookRequest {
    className: string;
    subject: string;
    chapter: string;
    file: File;
}

// Response types
interface UploadSuccessResponse {
    success: true;
    message: string;
    data: {
        fileName: string;
        blobPath: string;
        className: string;
        subject: string;
        chapter: string;
        fileSize: number;
        uploadedAt: string;
    };
}

interface UploadErrorResponse {
    success?: false;
    error: string;
    message: string;
}

type UploadResponse = UploadSuccessResponse | UploadErrorResponse;

// TypeScript upload function
async function uploadTextbookTS(
    className: string,
    subject: string,
    chapter: string,
    file: File
): Promise<UploadSuccessResponse> {
    const formData = new FormData();
    formData.append('className', className);
    formData.append('subject', subject);
    formData.append('chapter', chapter);
    formData.append('file', file);

    const response = await fetch('http://localhost:7071/api/upload/textbook', {
        method: 'POST',
        body: formData
    });

    const result: UploadResponse = await response.json();

    if (!response.ok || !result.success) {
        throw new Error(result.message || 'Upload failed');
    }

    return result as UploadSuccessResponse;
}
*/


// ============================================
// 7. CURL EXAMPLE (for testing)
// ============================================

/*
# Test upload with curl:
curl -X POST http://localhost:7071/api/upload/textbook \
  -F "className=Grade-10" \
  -F "subject=Mathematics" \
  -F "chapter=Chapter-1-Algebra" \
  -F "file=@/path/to/textbook.pdf"

# Expected response:
{
  "success": true,
  "message": "File uploaded successfully",
  "data": {
    "fileName": "textbook.pdf",
    "blobPath": "textbooks/Grade-10/Mathematics/Chapter-1-Algebra/textbook.pdf",
    "className": "Grade-10",
    "subject": "Mathematics",
    "chapter": "Chapter-1-Algebra",
    "fileSize": 1234567,
    "uploadedAt": "2025-11-13T12:00:00.000Z"
  }
}
*/


// ============================================
// 8. ERROR HANDLING BEST PRACTICES
// ============================================

async function uploadWithProperErrorHandling(className, subject, chapter, file) {
    // Validate inputs
    if (!file) {
        throw new Error('No file selected');
    }

    if (!file.name.toLowerCase().endsWith('.pdf')) {
        throw new Error('Only PDF files are allowed');
    }

    if (file.size > 50 * 1024 * 1024) { // 50MB limit
        throw new Error('File size must be less than 50MB');
    }

    if (!className?.trim() || !subject?.trim() || !chapter?.trim()) {
        throw new Error('Class, Subject, and Chapter are required');
    }

    const formData = new FormData();
    formData.append('className', className.trim());
    formData.append('subject', subject.trim());
    formData.append('chapter', chapter.trim());
    formData.append('file', file);

    try {
        const response = await fetch('http://localhost:7071/api/upload/textbook', {
            method: 'POST',
            body: formData
        });

        if (!response.ok) {
            const error = await response.json();
            throw new Error(error.message || `HTTP ${response.status}: ${response.statusText}`);
        }

        const result = await response.json();

        if (!result.success) {
            throw new Error(result.message || 'Upload failed');
        }

        return result;

    } catch (error) {
        // Network errors, timeout, etc.
        if (error.name === 'TypeError') {
            throw new Error('Network error - please check your connection');
        }
        throw error;
    }
}


// Export for ES6 modules
export { uploadTextbook, uploadWithProperErrorHandling };
