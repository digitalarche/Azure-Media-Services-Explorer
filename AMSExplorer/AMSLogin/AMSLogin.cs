﻿//----------------------------------------------------------------------------------------------
//    Copyright 2019 Microsoft Corporation
//
//    Licensed under the Apache License, Version 2.0 (the "License");
//    you may not use this file except in compliance with the License.
//    You may obtain a copy of the License at
//
//      http://www.apache.org/licenses/LICENSE-2.0
//
//    Unless required by applicable law or agreed to in writing, software
//    distributed under the License is distributed on an "AS IS" BASIS,
//    WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
//    See the License for the specific language governing permissions and
//    limitations under the License.
//---------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using System.Diagnostics;
using System.Reflection;
using Newtonsoft.Json;
using System.IO;
using Microsoft.IdentityModel.Clients.ActiveDirectory;
using Microsoft.Rest;
using Microsoft.Azure.Management.Media;
using Microsoft.Azure.Management.ResourceManager;
using Microsoft.Azure.Management.Storage;
using Microsoft.Azure.Management.Media.Models;
using Microsoft.Rest.Azure.Authentication;

namespace AMSExplorer
{
    public partial class AMSLogin : Form
    {
        ListCredentialsRPv3 CredentialList = new ListCredentialsRPv3();

        public string accountName;

        private CredentialsEntryV3 LoginInfo;
        private AzureEnvironmentV3 environment;

        public AMSClientV3 AMSClient { get; private set; }

        public AMSLogin()
        {
            InitializeComponent();
            this.Icon = Bitmaps.Azure_Explorer_ico;
        }

        private void AMSLogin_Load(object sender, EventArgs e)
        {

            // To clear list
            //Properties.Settings.Default.LoginListRPv3JSON = "";

            if (!string.IsNullOrWhiteSpace(Properties.Settings.Default.LoginListRPv3JSON))
            {
                string s = Properties.Settings.Default.LoginListRPv3JSON;
                // JSon deserialize
                CredentialList = (ListCredentialsRPv3)JsonConvert.DeserializeObject(Properties.Settings.Default.LoginListRPv3JSON, typeof(ListCredentialsRPv3));


                // Display accounts in the list
                CredentialList.MediaServicesAccounts.ForEach(c =>
                    AddItemToListviewAccounts(c)
                );
            }

            buttonExport.Enabled = (listViewAccounts.Items.Count > 0);

            accountmgtlink.Links.Add(new LinkLabel.Link(0, accountmgtlink.Text.Length, Constants.LinkAMSCreateAccount));
            linkLabelAADAut.Links.Add(new LinkLabel.Link(0, linkLabelAADAut.Text.Length, Constants.LinkAMSAADAut));

            // version
            labelVersion.Text = String.Format(labelVersion.Text, Assembly.GetExecutingAssembly().GetName().Version);

            DoEnableManualFields(false);
        }

        private void AddItemToListviewAccounts(CredentialsEntryV3 c)
        {
            var item = listViewAccounts.Items.Add(c.MediaService.Name);
            listViewAccounts.Items[item.Index].ForeColor = Color.Black;
            listViewAccounts.Items[item.Index].ToolTipText = null;
        }

        private void buttonDeleteAccount_Click(object sender, EventArgs e)
        {
            int index = listViewAccounts.SelectedIndices[0];
            if (index > -1)
            {
                CredentialList.MediaServicesAccounts.RemoveAt(index);
                SaveCredentialsToSettings();

                listViewAccounts.Items.Clear();
                CredentialList.MediaServicesAccounts.ForEach(c => AddItemToListviewAccounts(c));

                if (listViewAccounts.Items.Count > 0)
                {
                    listViewAccounts.Items[0].Selected = true;
                }
                else
                {
                    buttonDeleteAccountEntry.Enabled = false; // no selected item, so login button not active
                }
            }
        }

        private async void buttonLogin_Click(object sender, EventArgs e)
        {
            // code when used from pick-up
            LoginInfo = CredentialList.MediaServicesAccounts[listViewAccounts.SelectedIndices[0]];


            if (LoginInfo == null)
            {
                MessageBox.Show("Error", "Error", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
                //MessageBox.Show(string.Format("The {0} cannot be empty.", labelE1.Text), "Error", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
                return;
            }

            AMSClient = new AMSClientV3(LoginInfo.Environment, LoginInfo.AzureSubscriptionId, LoginInfo);

            AzureMediaServicesClient response = null;
            try
            {
                response = await AMSClient.ConnectAndGetNewClientV3();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }


            if (response == null) return;

            // let's save the credentials (SP) They may be updated by the user when connecting
            CredentialList.MediaServicesAccounts[listViewAccounts.SelectedIndices[0]] = AMSClient.credentialsEntry;
            SaveCredentialsToSettings();

            try
            {   // let's refresh storage accounts
                AMSClient.credentialsEntry.MediaService.StorageAccounts = AMSClient.AMSclient.Mediaservices.Get(AMSClient.credentialsEntry.ResourceGroup, AMSClient.credentialsEntry.AccountName).StorageAccounts;
                this.Cursor = Cursors.Default;

            }
            catch (Exception ex)
            {
                MessageBox.Show(Program.GetErrorMessage(ex), "Login error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                this.Cursor = Cursors.Default;
                return;
            }

            this.DialogResult = DialogResult.OK;  // form will close with OK result
                                                  // else --> form won't close...
        }


        private void listViewAccounts_SelectedIndexChanged(object sender, EventArgs e)
        {

            buttonDeleteAccountEntry.Enabled = (listViewAccounts.SelectedIndices.Count > 0); // no selected item, so login button not active
            buttonExport.Enabled = (listViewAccounts.Items.Count > 0);

            if (listViewAccounts.SelectedIndices.Count > 0) // one selected
            {
                LoginInfo = CredentialList.MediaServicesAccounts[listViewAccounts.SelectedIndices[0]];

                textBoxDescription.Text = LoginInfo.Description;
                textBoxAMSResourceId.Text = LoginInfo.MediaService.Id;
                textBoxLocation.Text = LoginInfo.MediaService.Location;
                textBoxAADtenantId.Text = LoginInfo.AadTenantId;

                DoEnableManualFields(false);
                groupBoxAADAutMode.Visible = true;

                radioButtonAADInteractive.Checked = !LoginInfo.UseSPAuth;
                radioButtonAADServicePrincipal.Checked = LoginInfo.UseSPAuth;
            }
        }

        private void DoClearFields()
        {
            textBoxAADtenantId.Text =
            textBoxAMSResourceId.Text =
            textBoxLocation.Text =
              string.Empty;

            radioButtonAADInteractive.Checked = true;

            int i = 0;
            foreach (var item in listViewAccounts.Items)
            {
                listViewAccounts.Items[i].Selected = false;
                i++;
            }
        }

        private void DoEnableManualFields(bool enable)
        {
            textBoxAADtenantId.Enabled =
            textBoxAMSResourceId.Enabled =
            textBoxLocation.Enabled =
                                    enable;
        }


        private void buttonExport_Click(object sender, EventArgs e)
        {
            bool exportAll = true;
            bool exportSPSecrets = false;
            var form = new ExportSettings();

            // There are more than one entry and one has been selected. 
            form.radioButtonAllEntries.Enabled = CredentialList.MediaServicesAccounts.Count > 1 && listViewAccounts.SelectedIndices.Count > 0;
            form.checkBoxIncludeSPSecrets.Checked = exportSPSecrets;


            if (form.ShowDialog() == DialogResult.OK)
            {
                exportAll = form.radioButtonAllEntries.Checked;
                exportSPSecrets = form.checkBoxIncludeSPSecrets.Checked;

                var jsonResolver = new PropertyRenameAndIgnoreSerializerContractResolver();
                List<string> properties = new List<string> { "EncryptedADSPClientSecret" };
                if (!exportSPSecrets)
                    properties.Add("ClearADSPClientSecret");
                jsonResolver.IgnoreProperty(typeof(CredentialsEntryV3), properties.ToArray()); // let's not export encrypted secret and may be clear secret

                JsonSerializerSettings settings = new JsonSerializerSettings();
                settings.NullValueHandling = NullValueHandling.Ignore;
                settings.Formatting = Newtonsoft.Json.Formatting.Indented;
                settings.ContractResolver = jsonResolver;

                DialogResult diares = saveFileDialog1.ShowDialog();
                if (diares == DialogResult.OK)
                {
                    try
                    {
                        if (exportAll)
                        {
                            System.IO.File.WriteAllText(saveFileDialog1.FileName, JsonConvert.SerializeObject(CredentialList, settings));
                        }
                        else
                        {
                            var copyEntry = new ListCredentialsRPv3();
                            copyEntry.MediaServicesAccounts.Add(CredentialList.MediaServicesAccounts[listViewAccounts.SelectedIndices[0]]);
                            System.IO.File.WriteAllText(saveFileDialog1.FileName, JsonConvert.SerializeObject(copyEntry, settings));
                        }
                    }
                    catch (Exception ex)

                    {
                        MessageBox.Show(ex.Message, AMSExplorer.Properties.Resources.AMSLogin_buttonExport_Click_Error, MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
            }
        }

        private void buttonImportAll_Click(object sender, EventArgs e)
        {
            bool mergesentries = false;

            if (CredentialList.MediaServicesAccounts.Count > 0) // There are entries. Let's ask if user want to delete them or merge
            {
                if (System.Windows.Forms.MessageBox.Show(AMSExplorer.Properties.Resources.AMSLogin_buttonImportAll_Click_ThereAreCurrentEntriesInTheListNDoYouWantReplaceThemWithTheNewOnesOrDoAMergeNNSelectYesToReplaceThemNoToMergeThem, AMSExplorer.Properties.Resources.AMSLogin_buttonImportAll_Click_ImportAndReplace, System.Windows.Forms.MessageBoxButtons.YesNo, MessageBoxIcon.Question) == System.Windows.Forms.DialogResult.No)
                {
                    mergesentries = true;
                }
            }

            DialogResult diares = openFileDialog1.ShowDialog();
            if (diares == DialogResult.OK)
            {
                if (Path.GetExtension(openFileDialog1.FileName).ToLower() == ".json")
                {
                    string json = System.IO.File.ReadAllText(openFileDialog1.FileName);

                    if (!mergesentries)
                    {
                        CredentialList.MediaServicesAccounts.Clear();
                        // let's purge entries if user does not want to keep them
                    }

                    var ImportedCredentialList = (ListCredentialsRPv3)JsonConvert.DeserializeObject(json, typeof(ListCredentialsRPv3));
                    CredentialList.MediaServicesAccounts.AddRange(ImportedCredentialList.MediaServicesAccounts);

                    listViewAccounts.Items.Clear();
                    //DoClearFields();
                    CredentialList.MediaServicesAccounts.ForEach(c => AddItemToListviewAccounts(c));
                    buttonExport.Enabled = (listViewAccounts.Items.Count > 0);

                    // let's save the list of credentials in settings
                    SaveCredentialsToSettings();

                }
            }
        }

        private void SaveCredentialsToSettings()
        {
            var jsonResolver = new PropertyRenameAndIgnoreSerializerContractResolver();
            jsonResolver.IgnoreProperty(typeof(CredentialsEntryV3), "ClearADSPClientSecret"); // let's not save the clear SP secret
            JsonSerializerSettings settings = new JsonSerializerSettings() { ContractResolver = jsonResolver };
            Properties.Settings.Default.LoginListRPv3JSON = JsonConvert.SerializeObject(CredentialList, settings);
            var t = JsonConvert.SerializeObject(CredentialList, settings);
            Program.SaveAndProtectUserConfig();
        }

        private void accountmgtlink_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            Process.Start(e.Link.LinkData as string);
        }

        private void AMSLogin_Shown(object sender, EventArgs e)
        {
            Program.CheckAMSEVersionV3();
        }

        private void panel1_Paint(object sender, PaintEventArgs e)
        {

        }

        private void CheckTextBox(object sender)
        {
            TextBox tb = (TextBox)sender;

            if (string.IsNullOrEmpty(tb.Text))
            {
                errorProvider1.SetError(tb, AMSExplorer.Properties.Resources.AMSLogin_CheckTextBox_ThisFieldIsMandatory);
            }
            else
            {
                errorProvider1.SetError(tb, String.Empty);
            }
        }


        private void listBoxAcounts_DoubleClick(object sender, EventArgs e)
        {
            // Proceed to log in to the selected account in the listbox
            buttonLogin_Click(sender, e);
        }


        private void textBoxAADtenant_Validating(object sender, CancelEventArgs e)
        {
            CheckTextBox(sender);
        }

        private void textBoxRestAPIEndpoint_Validating(object sender, CancelEventArgs e)
        {
            TextBox tb = (TextBox)sender;

            if (string.IsNullOrEmpty(tb.Text))
            {
                errorProvider1.SetError(tb, AMSExplorer.Properties.Resources.AMSLogin_CheckTextBox_ThisFieldIsMandatory);
            }
            else
            {
                bool Error = false;
                try
                {
                    var url = new Uri(tb.Text);
                }
                catch
                {
                    Error = true;
                }

                if (Error)
                {
                    errorProvider1.SetError(tb, "Please insert a valid URL");

                }
                else
                {
                    errorProvider1.SetError(tb, String.Empty);

                }
            }
        }


        private async void buttonConnectFullyInteractive_Click(object sender, EventArgs e)
        {
            var addaccount1 = new AddAMSAccount1();
            if (addaccount1.ShowDialog() == DialogResult.OK)
            {

                if (addaccount1.SelectedMode == AddAccountMode.BrowseSubscriptions)
                {
                    environment = addaccount1.GetEnvironment();

                    var authContext = new AuthenticationContext(
                    authority: environment.Authority,
                    validateAuthority: true);

                    AuthenticationResult accessToken;
                    try
                    {
                        accessToken = await authContext.AcquireTokenAsync(
                                                                             resource: environment.AADSettings.TokenAudience.ToString(),
                                                                             clientId: environment.ClientApplicationId,
                                                                             redirectUri: new Uri("urn:ietf:wg:oauth:2.0:oob"),
                                                                             parameters: new PlatformParameters(addaccount1.SelectUser ? PromptBehavior.SelectAccount : PromptBehavior.Auto, null)
                                                                             );
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return;
                    }

                    var credentials = new TokenCredentials(accessToken.AccessToken, "Bearer");

                    var subscriptionClient = new SubscriptionClient(environment.ArmEndpoint, credentials);
                    var subscriptions = subscriptionClient.Subscriptions.List();


                    /*
                    // test code  - briowser subscription with other tenants
                    var tenants = subscriptionClient.Tenants.List();

                    foreach (var tenant in tenants)
                    {
                        authContext = new AuthenticationContext(
                   authority: environment.Authority.Replace("common", tenant.TenantId),
                   validateAuthority: true);

                        try
                        {
                            accessToken = await authContext.AcquireTokenAsync(
                                                                                 resource: environment.AADSettings.TokenAudience.ToString(),
                                                                                 clientId: environment.ClientApplicationId,
                                                                                 redirectUri: new Uri("urn:ietf:wg:oauth:2.0:oob"),
                                                                                 parameters: new PlatformParameters(addaccount1.SelectUser ? PromptBehavior.SelectAccount : PromptBehavior.Auto, null)
                                                                                 );
                        }
                        catch (Exception ex)
                        {
                            MessageBox.Show(ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                            //return;
                        }

                        credentials = new TokenCredentials(accessToken.AccessToken, "Bearer");

                         subscriptionClient = new SubscriptionClient(environment.ArmEndpoint, credentials);
                        subscriptions = subscriptionClient.Subscriptions.List();
                        var addaccount3 = new AddAMSAccount2Browse(credentials, subscriptions, environment);
                        addaccount3.ShowDialog();

                    }
                                                         
                    // end test code
                    */

                    var addaccount2 = new AddAMSAccount2Browse(credentials, subscriptions, environment);
                    if (addaccount2.ShowDialog() == DialogResult.OK)
                    {
                        // Getting Media Services accounts...
                        var mediaServicesClient = new AzureMediaServicesClient(environment.ArmEndpoint, credentials);

                        var entry = new CredentialsEntryV3(addaccount2.selectedAccount,
                            environment,
                            addaccount1.SelectUser ? PromptBehavior.SelectAccount : PromptBehavior.Auto,
                            false,
                            null,
                            false
                            );
                        CredentialList.MediaServicesAccounts.Add(entry);
                        AddItemToListviewAccounts(entry);

                        SaveCredentialsToSettings();
                    }
                    else
                    {
                        return;
                    }
                }


                // Get info from the Azure CLI JSON
                else if (addaccount1.SelectedMode == AddAccountMode.FromAzureCliJson)
                {
                    string example = @"{
  ""AadClientId"": ""00000000-0000-0000-0000-000000000000"",
  ""AadEndpoint"": ""https://login.microsoftonline.com"",
  ""AadSecret"": ""00000000-0000-0000-0000-000000000000"",
  ""AadTenantId"": ""00000000-0000-0000-0000-000000000000"",
  ""AccountName"": ""amsaccount"",
  ""ArmAadAudience"": ""https://management.core.windows.net/"",
  ""ArmEndpoint"": ""https://management.azure.com/"",
  ""Region"": ""West Europe"",
  ""ResourceGroup"": ""amsResourceGroup"",
  ""SubscriptionId"": ""00000000-0000-0000-0000-000000000000""
}";
                    var form = new EditorXMLJSON("Enter the JSON output of Azure Cli Service Principal creation (az ams account sp create)", example, true, false, true, "The Service Principal secret is stored encrypted in the application settings.");

                    if (form.ShowDialog() == DialogResult.OK)
                    {
                        JsonFromAzureCli json = null;
                        try
                        {
                            json = (JsonFromAzureCli)JsonConvert.DeserializeObject(form.TextData, typeof(JsonFromAzureCli));

                        }
                        catch (Exception ex)
                        {
                            MessageBox.Show(ex.Message, "Error reading the json", MessageBoxButtons.OK, MessageBoxIcon.Error);
                            return;
                        }
                        string resourceId = string.Format("/subscriptions/{0}/resourceGroups/{1}/providers/Microsoft.Media/mediaservices/{2}", json.SubscriptionId, json.ResourceGroup, json.AccountName);
                        string AADtenantId = json.AadTenantId;

                        var aadSettings = new ActiveDirectoryServiceSettings()
                        {
                            AuthenticationEndpoint = json.AadEndpoint,
                            TokenAudience = json.ArmAadAudience,
                            ValidateAuthority = true
                        };

                        var env = new AzureEnvironmentV3(AzureEnvType.Custom) { AADSettings = aadSettings, ArmEndpoint = json.ArmEndpoint };

                        var entry = new CredentialsEntryV3(
                                                        new SubscriptionMediaService(resourceId, json.AccountName, null, null, json.Region),
                                                        env,
                                                        PromptBehavior.Auto,
                                                        true,
                                                        AADtenantId,
                                                        false
                                                        );

                        entry.ADSPClientId = json.AadClientId;
                        entry.ClearADSPClientSecret = json.AadSecret;

                        CredentialList.MediaServicesAccounts.Add(entry);
                        AddItemToListviewAccounts(entry);

                        SaveCredentialsToSettings();
                    }
                    else
                    {
                        return;
                    }

                }
                else if (addaccount1.SelectedMode == AddAccountMode.ManualEntry)
                {
                    var form = new AddAMSAccount2Manual();
                    if (form.ShowDialog() == DialogResult.OK)
                    {
                        string accountnamecc = form.textBoxAMSResourceId.Text.Split('/').Last();

                        var entry = new CredentialsEntryV3(
                                                        new SubscriptionMediaService(form.textBoxAMSResourceId.Text, accountnamecc, null, null, form.textBoxLocation.Text),
                                                        addaccount1.GetEnvironment(),
                                                        PromptBehavior.Auto,
                                                        radioButtonAADServicePrincipal.Checked,
                                                        form.textBoxAADtenantId.Text,
                                                        true
                                                        );


                        CredentialList.MediaServicesAccounts.Add(entry);
                        AddItemToListviewAccounts(entry);

                        SaveCredentialsToSettings();
                    }
                    else return;
                }
            }
        }

        private void buttonManualEntry_Click(object sender, EventArgs e)
        {
            tabControlAMS.Enabled = true;
            DoClearFields();
            DoEnableManualFields(true);
            labelADTenant.Visible = textBoxAADtenantId.Visible = false; // onsly show tenant if SP is used
            radioButtonAADInteractive.Checked = true;
            groupBoxAADAutMode.Visible = true;
        }

        private void radioButtonAADServicePrincipal_CheckedChanged(object sender, EventArgs e)
        {
            labelADTenant.Visible = textBoxAADtenantId.Visible = radioButtonAADServicePrincipal.Checked; // onsly show tenant if SP is used

        }

        private void buttonImportSPJson_Click(object sender, EventArgs e)
        {
            //  Azure portal / AMS Account / Properties. Example : /subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/amsResourceGroup/providers/Microsoft.Media/mediaservices/amsaccount
        }

        private void textBoxDescription_TextChanged(object sender, EventArgs e)
        {
            CredentialList.MediaServicesAccounts[listViewAccounts.SelectedIndices[0]].Description = textBoxDescription.Text;
            SaveCredentialsToSettings();
        }

        private void linkLabelAMSOfflineDoc_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            Process.Start(Application.StartupPath + @"\HelpFiles\" + @"AMSv3doc.pdf");
        }
    }
}
