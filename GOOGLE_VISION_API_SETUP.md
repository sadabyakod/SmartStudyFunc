# How to Get Google Cloud Vision API Key

## Quick Setup Guide

### Step 1: Access Google Cloud Console
- Go to: **https://console.cloud.google.com/**
- Sign in with your Google account

### Step 2: Create or Select a Project
1. Click on the project dropdown (top of the page)
2. Either select an existing project or click **"NEW PROJECT"**
3. Name it (e.g., `SmartStudy-OCR`)
4. Click **"CREATE"**

### Step 3: Enable Cloud Vision API
1. Go to: **https://console.cloud.google.com/apis/library**
2. Search for **"Cloud Vision API"**
3. Click on **"Cloud Vision API"**
4. Click **"ENABLE"** button
5. Wait for the API to be enabled (may take a minute)

### Step 4: Create API Key
1. Go to: **https://console.cloud.google.com/apis/credentials**
2. Click **"+ CREATE CREDENTIALS"** at the top
3. Select **"API Key"**
4. **Copy the generated API key immediately**
5. Save it securely (you'll need it in Step 6)

### Step 5 (Optional but Recommended): Restrict API Key
For security, restrict the API key:
1. Click **"RESTRICT KEY"** after creation
2. Give it a name (e.g., `SmartStudy-Vision-Key`)
3. Under **"API restrictions"**, select **"Restrict key"**
4. Check only **"Cloud Vision API"**
5. Click **"SAVE"**

### Step 6: Configure in Azure Function App

Run this PowerShell command to configure the API key:

```powershell
az functionapp config appsettings set `
  --name smartstudy-func `
  --resource-group rg-smartstudy-dev `
  --settings "GoogleCloud__ApiKey=YOUR_API_KEY_HERE"
```

**Replace `YOUR_API_KEY_HERE` with your actual API key from Step 4**

### Step 7: Restart Function App

```powershell
az functionapp restart --name smartstudy-func --resource-group rg-smartstudy-dev
```

### Step 8: Test the Upload

Run the test script:
```powershell
cd c:\SmartStudyFunc
.\test-upload-fixed.ps1
```

---

## ⚠️ Important Notes

### Billing Requirements
- **Google Cloud Vision API requires billing to be enabled**
- You need to add a payment method to your Google Cloud account
- Go to: https://console.cloud.google.com/billing

### Pricing (as of 2025)
- **First 1,000 pages/month: FREE** ✅
- **After 1,000 pages: $1.50 per 1,000 pages**
- Text detection (OCR) counts as 1 unit per page

### Security Best Practices
- ✅ Keep your API key secure
- ✅ Never commit API keys to Git repositories
- ✅ Use API restrictions to limit key usage
- ✅ Rotate keys periodically
- ✅ Consider using Service Account authentication for production

---

## Alternative: Service Account Authentication

For production environments, Service Account authentication is more secure:

1. Create a service account in Google Cloud Console
2. Download the JSON key file
3. Upload to Azure Function App as a file
4. Set environment variable: `GOOGLE_APPLICATION_CREDENTIALS=/path/to/key.json`

The code already supports both API Key and Service Account authentication.

---

## Verification

After configuration, check if it's working:

```powershell
# Check if the setting is configured
az functionapp config appsettings list --name smartstudy-func --resource-group rg-smartstudy-dev --query "[?name=='GoogleCloud__ApiKey'].{name:name, value:value}"

# Upload a test file and check status
.\test-upload-fixed.ps1
```

The error should change from "API key not valid" to successful OCR processing.

---

## Troubleshooting

### Error: "API key not valid"
- Double-check the API key is correct
- Ensure Cloud Vision API is enabled
- Verify billing is enabled in Google Cloud

### Error: "Quota exceeded"
- Check your Google Cloud quotas
- Verify billing is set up correctly

### Error: "Blob not found"
- This means the API key issue is fixed!
- Check blob storage permissions

---

## Documentation Links

- **Vision API Overview**: https://cloud.google.com/vision/docs
- **Vision API Pricing**: https://cloud.google.com/vision/pricing
- **API Key Best Practices**: https://cloud.google.com/docs/authentication/api-keys
- **Service Accounts**: https://cloud.google.com/iam/docs/service-accounts

---

## Current Status

✅ **Fixed Issues:**
- Upload API working
- Blob storage path correct (`students-answer-sheets/{examId}/{studentId}/{timestamp}_{guid}.ext`)
- Database records saving
- Queue processing working
- Blob access issue FIXED

⚠️ **Remaining:**
- Configure valid Google Cloud Vision API key
- Enable billing in Google Cloud (if not already done)
