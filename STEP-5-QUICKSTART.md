# Step-5 Quick Reference

## Quick Start (3 Steps)

### 1. Configure Azure OpenAI Settings
```powershell
.\tools\set-azureopenai-config.ps1
```
Follow the interactive prompts to enter your:
- Azure OpenAI Endpoint
- API Key
- Embedding Deployment Name
- Chat Deployment Name

### 2. Validate Configuration
```powershell
.\tools\test-azureopenai-config.ps1
```
Confirms your settings work by making a test API call.

### 3. Run the Function
```powershell
func start
```
Your SmartStudyFunc is ready with real Azure OpenAI embeddings!

---

## Common Commands

### Configure with Parameters
```powershell
.\tools\set-azureopenai-config.ps1 `
  -Endpoint "https://my-resource.openai.azure.com/" `
  -ApiKey "abc123..." `
  -EmbeddingDeployment "text-embedding-3-large" `
  -ChatDeployment "gpt-4o-mini"
```

### Validate Without Rebuilding
```powershell
.\tools\test-azureopenai-config.ps1 -SkipBuild
```

### View Configuration Template
```powershell
Get-Content local.settings.template.json
```

---

## Where to Find Azure Values

### Endpoint & API Key
1. [Azure Portal](https://portal.azure.com) → Your OpenAI Resource
2. **Keys and Endpoint** section
3. Copy **Endpoint** and **Key 1**

### Deployment Names
1. [Azure AI Studio](https://oai.azure.com/)
2. Select your resource
3. **Deployments** tab
4. Note exact names (case-sensitive!)

---

## Troubleshooting One-Liners

| Issue | Quick Fix |
|-------|-----------|
| ✗ Deployment Not Found | Check deployment name is EXACTLY correct (case-sensitive) |
| ✗ Unauthorized | Regenerate API key in Azure Portal and reconfigure |
| ⚠ Rate Limited | Wait a few minutes, or increase quota in Azure Portal |
| ⚠ Not Configured | Run `.\tools\set-azureopenai-config.ps1` |

---

## Files Created

- `local.settings.template.json` - Template with placeholders
- `tools/set-azureopenai-config.ps1` - Configuration script
- `tools/test-azureopenai-config.ps1` - Validation script
- `STEP-5-SUMMARY.md` - Full implementation details
- `README.md` - Updated with Step-5 section

---

## Validation Results

### ✓ Success
```
✓ Configuration file contains required settings
✓ Project built successfully
✓ SUCCESS! Azure OpenAI connection is working

  Deployment: text-embedding-3-large
  Embedding dimensions: 3072
```
**→ You're ready to run `func start`**

### ✗ Deployment Not Found
```
❌ DEPLOYMENT NOT FOUND

The deployment 'text-embedding-3-large' was not found
```
**→ Fix**: Verify exact deployment name in Azure AI Studio

### ✗ Unauthorized
```
❌ UNAUTHORIZED - API Key Issue

Your API key appears to be invalid or expired
```
**→ Fix**: Get new key from Azure Portal → Keys and Endpoint

---

## Security Reminders

- ✓ `local.settings.json` is in `.gitignore` (not committed)
- ✓ Use template file for team sharing
- ✓ Never commit API keys to source control
- ✗ If you already committed keys, regenerate them in Azure Portal

---

## What's Next?

After successful validation:

1. **Start Function**: `func start`
2. **Upload PDF**: Use Azure Storage Explorer or portal
3. **Query Data**: Call `/api/search` endpoint
4. **Monitor Logs**: Watch Azure OpenAI API calls in console

---

## Support

- **Full Documentation**: See README.md → Step-5 section
- **Implementation Details**: See STEP-5-SUMMARY.md
- **Configuration Template**: See local.settings.template.json
