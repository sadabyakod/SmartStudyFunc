# GitHub Actions Setup for Azure Deployment with RBAC

## Current Error
```
Error: No credentials found. Add an Azure login action before this action.
```

## Solution

The workflow file `.github/workflows/master_studyai-ingestion-345.yml` is correctly configured for RBAC authentication, but the GitHub secrets need to be verified.

### Option 1: Re-download the Publish Profile (Easiest)

1. Go to Azure Portal → Function App `studyai-ingestion-345`
2. Click **Deployment Center** in the left menu
3. Click **Manage publish profile** → **Download publish profile**
4. The workflow will automatically update with the correct credentials

### Option 2: Verify GitHub Secrets

1. Go to your GitHub repository: https://github.com/sadabyakod/SmartStudyFunc
2. Click **Settings** → **Secrets and variables** → **Actions**
3. Verify these secrets exist:
   - `AZUREAPPSERVICE_CLIENTID_F64962A250074B04B168FD869E5F7910`
   - `AZUREAPPSERVICE_TENANTID_752C9C953BB84EB5A2C5A46CDECCB67B`
   - `AZUREAPPSERVICE_SUBSCRIPTIONID_6B585BE6041F4C8B936AB316820DBB21`

### Option 3: Manually Configure RBAC (Advanced)

If the secrets are missing, you need to create a Service Principal:

#### Step 1: Get your Subscription ID and Tenant ID

```powershell
# Login to Azure
az login

# Get subscription ID
az account show --query id -o tsv

# Get tenant ID
az account show --query tenantId -o tsv
```

#### Step 2: Create Service Principal

```powershell
# Replace with your values
$subscriptionId = "<your-subscription-id>"
$resourceGroup = "<your-resource-group-name>"
$appName = "studyai-ingestion-345"

# Create service principal with Contributor role on the Function App
az ad sp create-for-rbac --name "github-actions-studyai-ingestion" `
  --role contributor `
  --scopes /subscriptions/$subscriptionId/resourceGroups/$resourceGroup/providers/Microsoft.Web/sites/$appName `
  --json-auth
```

This will output JSON like:
```json
{
  "clientId": "xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx",
  "clientSecret": "xxxxxxxxxxxxxxxxxxxxxxxxxxxxx",
  "subscriptionId": "xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx",
  "tenantId": "xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx"
}
```

#### Step 3: Add Secrets to GitHub

1. Go to: https://github.com/sadabyakod/SmartStudyFunc/settings/secrets/actions
2. Click **New repository secret**
3. Add these secrets (use the values from the JSON output):
   - Name: `AZUREAPPSERVICE_CLIENTID_F64962A250074B04B168FD869E5F7910`
     Value: `<clientId from JSON>`
   - Name: `AZUREAPPSERVICE_TENANTID_752C9C953BB84EB5A2C5A46CDECCB67B`
     Value: `<tenantId from JSON>`
   - Name: `AZUREAPPSERVICE_SUBSCRIPTIONID_6B585BE6041F4C8B936AB316820DBB21`
     Value: `<subscriptionId from JSON>`

### Option 4: Use Publish Profile Instead (Simpler Alternative)

If RBAC is too complex, you can switch to publish profile authentication:

#### Step 1: Download Publish Profile

1. Go to Azure Portal → Function App `studyai-ingestion-345`
2. Click **Get publish profile** (top menu)
3. Save the downloaded `.PublishSettings` file

#### Step 2: Add Secret to GitHub

1. Go to: https://github.com/sadabyakod/SmartStudyFunc/settings/secrets/actions
2. Click **New repository secret**
3. Name: `AZURE_FUNCTIONAPP_PUBLISH_PROFILE`
4. Value: Open the `.PublishSettings` file and paste the entire XML content

#### Step 3: Update Workflow File

Update `.github/workflows/master_studyai-ingestion-345.yml`:

```yaml
    steps:
      - name: 'Checkout GitHub Action'
        uses: actions/checkout@v4

      - name: Setup DotNet ${{ env.DOTNET_VERSION }} Environment
        uses: actions/setup-dotnet@v1
        with:
          dotnet-version: ${{ env.DOTNET_VERSION }}

      - name: 'Resolve Project Dependencies Using Dotnet'
        shell: pwsh
        run: |
          pushd './${{ env.AZURE_FUNCTIONAPP_PACKAGE_PATH }}'
          dotnet build --configuration Release --output ./output
          popd
      
      # REMOVE the Azure login step
      # - name: Login to Azure
      #   uses: azure/login@v2
      #   ...

      - name: 'Run Azure Functions Action'
        uses: Azure/functions-action@v1
        id: fa
        with:
          app-name: 'studyai-ingestion-345'
          slot-name: 'Production'
          package: '${{ env.AZURE_FUNCTIONAPP_PACKAGE_PATH }}/output'
          publish-profile: ${{ secrets.AZURE_FUNCTIONAPP_PUBLISH_PROFILE }}  # ADD THIS LINE
```

## Recommended: Use Azure Portal to Re-sync

The easiest solution:

1. Go to Azure Portal → Function App `studyai-ingestion-345`
2. Click **Deployment Center**
3. If GitHub is already connected, click **Disconnect**
4. Click **GitHub** → Authorize → Select repository `sadabyakod/SmartStudyFunc`
5. Azure will automatically:
   - Create the workflow file with correct credentials
   - Add the required secrets to GitHub
   - Trigger the first deployment

## Verify Deployment

After fixing the secrets:

1. Go to: https://github.com/sadabyakod/SmartStudyFunc/actions
2. Find the latest workflow run
3. Check if it completes successfully
4. Verify the function is deployed in Azure Portal

## Troubleshooting

If deployment still fails:

1. Check the workflow logs in GitHub Actions
2. Verify the Function App name is correct: `studyai-ingestion-345`
3. Ensure the service principal has **Contributor** role on the Function App
4. Check that the subscription ID matches your Azure subscription

## Quick Fix Command

If you want to re-trigger the deployment after fixing secrets:

```powershell
# Commit any changes and push
git add .
git commit -m "Update deployment configuration"
git push origin master
```

This will automatically trigger the GitHub Actions workflow.
