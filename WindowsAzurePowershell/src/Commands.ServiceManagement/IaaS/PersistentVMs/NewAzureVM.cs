﻿// ----------------------------------------------------------------------------------
//
// Copyright Microsoft Corporation
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
// http://www.apache.org/licenses/LICENSE-2.0
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
// ----------------------------------------------------------------------------------

namespace Microsoft.WindowsAzure.Commands.ServiceManagement.IaaS.PersistentVMs
{
    using System;
    using System.Net;
    using System.Linq;
    using System.Management.Automation;
    using AutoMapper;
    using Utilities.Common;
    using Management.Compute;
    using Management.Compute.Models;
    using Model;
    using Model.PersistentVMModel;
    using Storage;
    using Helpers;
    using Properties;

    [Cmdlet(VerbsCommon.New, "AzureVM", DefaultParameterSetName = "ExistingService"), OutputType(typeof(ManagementOperationContext))]
    public class NewAzureVMCommand : IaaSDeploymentManagementCmdletBase
    {
        private bool createdDeployment;

        [Parameter(Mandatory = true, ParameterSetName = "CreateService", ValueFromPipeline = true, ValueFromPipelineByPropertyName = true, HelpMessage = "Service Name")]
        [Parameter(Mandatory = true, ParameterSetName = "ExistingService", ValueFromPipeline = true, ValueFromPipelineByPropertyName = true, HelpMessage = "Service Name")]
        [ValidateNotNullOrEmpty]
        public override string ServiceName
        {
            get;
            set;
        }

        [Parameter(ValueFromPipelineByPropertyName = true, ParameterSetName = "CreateService", HelpMessage = "Required if AffinityGroup is not specified. The data center region where the cloud service will be created.")]
        [ValidateNotNullOrEmpty]
        public string Location
        {
            get;
            set;
        }

        [Parameter(Mandatory = false, ValueFromPipelineByPropertyName = true, ParameterSetName = "CreateService", HelpMessage = "Required if Location is not specified. The name of an existing affinity group associated with this subscription.")]
        [ValidateNotNullOrEmpty]
        public string AffinityGroup
        {
            get;
            set;
        }

        [Parameter(Mandatory = false, ValueFromPipelineByPropertyName = true, ParameterSetName = "CreateService", HelpMessage = "The label may be up to 100 characters in length. Defaults to Service Name.")]
        [ValidateNotNullOrEmpty]
        public string ServiceLabel
        {
            get;
            set;
        }

        [Parameter(Mandatory = false, ValueFromPipelineByPropertyName = true, ParameterSetName = "CreateService", HelpMessage = "A description for the cloud service. The description may be up to 1024 characters in length.")]
        [ValidateNotNullOrEmpty]
        public string ServiceDescription
        {
            get;
            set;
        }

        [Parameter(Mandatory = false, ParameterSetName = "CreateService", ValueFromPipelineByPropertyName = true, HelpMessage = "Deployment Label. Will default to service name if not specified.")]
        [Parameter(Mandatory = false, ParameterSetName = "ExistingService", ValueFromPipelineByPropertyName = true, HelpMessage = "Deployment Label. Will default to service name if not specified.")]
        [ValidateNotNullOrEmpty]
        public string DeploymentLabel
        {
            get;
            set;
        }

        [Parameter(Mandatory = false, ParameterSetName = "CreateService", ValueFromPipelineByPropertyName = true, HelpMessage = "Deployment Name. Will default to service name if not specified.")]
        [Parameter(Mandatory = false, ParameterSetName = "ExistingService", ValueFromPipelineByPropertyName = true, HelpMessage = "Deployment Name. Will default to service name if not specified.")]
        [ValidateNotNullOrEmpty]
        public string DeploymentName
        {
            get;
            set;
        }

        [Parameter(Mandatory = false, ParameterSetName = "CreateService", HelpMessage = "Virtual network name.")]
        [Parameter(Mandatory = false, ParameterSetName = "ExistingService", HelpMessage = "Virtual network name.")]
        public string VNetName
        {
            get;
            set;
        }

        [Parameter(Mandatory = false, ParameterSetName = "CreateService", ValueFromPipeline = true, ValueFromPipelineByPropertyName = true, HelpMessage = "DNS Settings for Deployment.")]
        [Parameter(Mandatory = false, ParameterSetName = "ExistingService", ValueFromPipeline = true, ValueFromPipelineByPropertyName = true, HelpMessage = "DNS Settings for Deployment.")]
        [ValidateNotNullOrEmpty]
        public DnsServer[] DnsSettings
        {
            get;
            set;
        }

        [Parameter(Mandatory = true, ParameterSetName = "CreateService", ValueFromPipeline = true, ValueFromPipelineByPropertyName = true, HelpMessage = "List of VMs to Deploy.")]
        [Parameter(Mandatory = true, ParameterSetName = "ExistingService", ValueFromPipeline = true, ValueFromPipelineByPropertyName = true, HelpMessage = "List of VMs to Deploy.")]
        [ValidateNotNullOrEmpty]
        public PersistentVM[] VMs
        {
            get;
            set;
        }

        [Parameter(Mandatory = false, HelpMessage = "Waits for VM to boot")]
        [ValidateNotNullOrEmpty]
        public SwitchParameter WaitForBoot
        {
            get;
            set;
        }

        public void NewAzureVMProcess()
        {
            Mapper.Initialize(m => m.AddProfile<ServiceManagementProfile>());

            SubscriptionData currentSubscription = this.GetCurrentSubscription();

            CloudStorageAccount currentStorage = null;
            try
            {
                currentStorage = currentSubscription.GetCurrentStorageAccount();
            }
            catch (Exception ex) // couldn't access
            {
                throw new ArgumentException(Resources.CurrentStorageAccountIsNotAccessible, ex);
            }
            if (currentStorage == null) // not set
            {
                throw new ArgumentException(Resources.CurrentStorageAccountIsNotSet);
            }

            try
            {
                if (this.ParameterSetName.Equals("CreateService", StringComparison.OrdinalIgnoreCase))
                {
                    var parameter = new HostedServiceCreateParameters
                    {
                        AffinityGroup = this.AffinityGroup,
                        Location = this.Location,
                        ServiceName = this.ServiceName,
                        Description = this.ServiceDescription ??
                                        String.Format("Implicitly created hosted service{0}",DateTime.Now.ToUniversalTime().ToString("yyyy-MM-dd HH:mm")),
                        Label = this.ServiceLabel ?? this.ServiceName
                    };
                    ExecuteClientActionNewSM(
                        parameter,
                        CommandRuntime + " - Create Cloud Service",
                        () => this.ComputeClient.HostedServices.Create(parameter),
                        (s, response) => ContextFactory<OperationResponse, ManagementOperationContext>(response, s));
                }
            }
            catch (CloudException ex)
            {
                this.WriteErrorDetails(ex);
                return;
            }

            foreach (var vm in from v in VMs let configuration = v.ConfigurationSets.OfType<WindowsProvisioningConfigurationSet>().FirstOrDefault() where configuration != null select v)
            {
                if (vm.WinRMCertificate != null)
                {
                    if(!CertUtilsNewSM.HasExportablePrivateKey(vm.WinRMCertificate))
                    {
                        throw new ArgumentException(Resources.WinRMCertificateDoesNotHaveExportablePrivateKey);
                    }
                    var operationDescription = string.Format(Resources.AzureVMUploadingWinRMCertificate, CommandRuntime, vm.WinRMCertificate.Thumbprint);
                    var parameters = CertUtilsNewSM.Create(vm.WinRMCertificate);
                    ExecuteClientActionNewSM(
                        null,
                        operationDescription,
                        () => this.ComputeClient.ServiceCertificates.Create(this.ServiceName, parameters),
                        (s, r) => ContextFactory<ComputeOperationStatusResponse, ManagementOperationContext>(r, s));

                }
                var certificateFilesWithThumbprint = from c in vm.X509Certificates
                    select new
                           {
                               c.Thumbprint,
                               CertificateFile = CertUtilsNewSM.Create(c, vm.NoExportPrivateKey)
                           };
                foreach (var current in certificateFilesWithThumbprint.ToList())
                {
                    var operationDescription = string.Format(Resources.AzureVMCommandUploadingCertificate, CommandRuntime, current.Thumbprint);
                    ExecuteClientActionNewSM(
                        null,
                        operationDescription,
                        () => this.ComputeClient.ServiceCertificates.Create(this.ServiceName, current.CertificateFile),
                        (s, r) => ContextFactory<ComputeOperationStatusResponse, ManagementOperationContext>(r, s));
                }
            }

            var persistentVMs = this.VMs.Select(vm => CreatePersistentVMRoleNewSM(vm, currentStorage)).ToList();

            // If the current deployment doesn't exist set it create it
            if (CurrentDeploymentNewSM == null)
            {
                try
                {
                    var parameters = new VirtualMachineCreateDeploymentParameters
                    {
                        DeploymentSlot = DeploymentSlot.Production,
                        Name = this.DeploymentName ?? this.ServiceName,
                        Label = this.DeploymentLabel ?? this.ServiceName,
                        VirtualNetworkName = this.VNetName
                    };
                    parameters.Roles.Add(persistentVMs[0]);

                    if (this.DnsSettings != null)
                    {
                        //TODO: https://github.com/WindowsAzure/azure-sdk-for-net-pr/issues/114
                        parameters.DnsSettings = new Management.Compute.Models.DnsSettings();

                        foreach (var dns in this.DnsSettings)
                        {
                            parameters.DnsSettings.DnsServers.Add(dns.Name, IPAddress.Parse(dns.Address));
                        }
                    }

                    var operationDescription = string.Format(Resources.AzureVMCommandCreateDeploymentWithVM, CommandRuntime, persistentVMs[0].RoleName);
                    ExecuteClientActionNewSM(
                        parameters,
                        operationDescription,
                        () => this.ComputeClient.VirtualMachines.CreateDeployment(this.ServiceName, parameters),
                        (s, response) => ContextFactory<ComputeOperationStatusResponse, ManagementOperationContext>(response, s));

                    if(this.WaitForBoot.IsPresent)
                    {
                        WaitForRoleToBoot(persistentVMs[0].RoleName);
                    }
                }
                catch (CloudException ex)
                {
                    if (ex.Response.StatusCode == HttpStatusCode.NotFound)
                    {
                        throw new Exception(Resources.ServiceDoesNotExistSpecifyLocationOrAffinityGroup);
                    }
                    else
                    {
                        this.WriteErrorDetails(ex);
                    }
                    return;
                }

                this.createdDeployment = true;
            }
            else
            {
                if (this.VNetName != null || this.DnsSettings != null || !string.IsNullOrEmpty(this.DeploymentLabel) || !string.IsNullOrEmpty(this.DeploymentName))
                {
                    WriteWarning(Resources.VNetNameDnsSettingsDeploymentLabelDeploymentNameCanBeSpecifiedOnNewDeployments);
                }
            }

            if (this.createdDeployment == false && CurrentDeploymentNewSM != null)
            {
                this.DeploymentName = CurrentDeploymentNewSM.Name;
            }

            int startingVM = this.createdDeployment ? 1 : 0;

            for (int i = startingVM; i < persistentVMs.Count; i++)
            {
                var operationDescription = string.Format(Resources.AzureVMCommandCreateVM, CommandRuntime, persistentVMs[i].RoleName);
                VirtualMachineRoleSize roleSizeResult;
                if (!Enum.TryParse(persistentVMs[i].RoleSize, true, out roleSizeResult))
                {
                    throw new ArgumentOutOfRangeException("RoleSize:" + persistentVMs[i].RoleSize);
                }

                //TODO: https://github.com/WindowsAzure/azure-sdk-for-net-pr/issues/115
                //TOOD: https://github.com/WindowsAzure/azure-sdk-for-net-pr/issues/118
                var parameter = new VirtualMachineCreateParameters
                {
                    AvailabilitySetName = persistentVMs[i].AvailabilitySetName,
                    OSVirtualHardDisk = Mapper.Map(persistentVMs[i].OSVirtualHardDisk, new Management.Compute.Models.OSVirtualHardDisk()),
                    RoleName = persistentVMs[i].RoleName,
                    RoleSize = roleSizeResult,
                };
                parameter.DataVirtualHardDisks.ForEach(c => persistentVMs[i].DataVirtualHardDisks.Add(Mapper.Map(c, new Management.Compute.Models.DataVirtualHardDisk())));
                parameter.ConfigurationSets.ForEach(c => persistentVMs[i].ConfigurationSets.Add(Mapper.Map(c, new Management.Compute.Models.ConfigurationSet())));

                ExecuteClientActionNewSM(
                    persistentVMs[i],
                    operationDescription,
                    () => this.ComputeClient.VirtualMachines.Create(this.ServiceName, this.DeploymentName, parameter),
                    (s, response) => ContextFactory<ComputeOperationStatusResponse, ManagementOperationContext>(response, s));
            }

            if(this.WaitForBoot.IsPresent)
            {
                for (int i = startingVM; i < persistentVMs.Count; i++)
                {
                    WaitForRoleToBoot(persistentVMs[i].RoleName);
                }
            }
        }

        private Management.Compute.Models.Role CreatePersistentVMRoleNewSM(PersistentVM persistentVM, CloudStorageAccount currentStorage)
        {
            if (!string.IsNullOrEmpty(persistentVM.OSVirtualHardDisk.DiskName) && !NetworkConfigurationSetBuilder.HasNetworkConfigurationSet(persistentVM.ConfigurationSets))
            {
//                var networkConfigurationSetBuilder = new NetworkConfigurationSetBuilder(persistentVM.ConfigurationSets);
//
//                Disk disk = this.Channel.GetDisk(CurrentSubscription.SubscriptionId, persistentVM.OSVirtualHardDisk.DiskName);
//                if (disk.OS == OS.Windows && !persistentVM.NoRDPEndpoint)
//                {
//                    networkConfigurationSetBuilder.AddRdpEndpoint();
//                }
//                else if (disk.OS == OS.Linux && !persistentVM.NoSSHEndpoint)
//                {
//                    networkConfigurationSetBuilder.AddSshEndpoint();
//                }
            }

            var mediaLinkFactory = new MediaLinkFactory(currentStorage, this.ServiceName, persistentVM.RoleName);

            if (persistentVM.OSVirtualHardDisk.MediaLink == null && string.IsNullOrEmpty(persistentVM.OSVirtualHardDisk.DiskName))
            {
                persistentVM.OSVirtualHardDisk.MediaLink = mediaLinkFactory.Create();
            }

            foreach (var datadisk in persistentVM.DataVirtualHardDisks.Where(d => d.MediaLink == null && string.IsNullOrEmpty(d.DiskName)))
            {
                datadisk.MediaLink = mediaLinkFactory.Create();
            }

            //TODO: https://github.com/WindowsAzure/azure-sdk-for-net-pr/issues/115
            //TODO: https://github.com/WindowsAzure/azure-sdk-for-net-pr/issues/117
            //TODO: https://github.com/WindowsAzure/azure-sdk-for-net-pr/issues/118
            var result = new Management.Compute.Models.Role
            {
                AvailabilitySetName = persistentVM.AvailabilitySetName,
                OSVirtualHardDisk = Mapper.Map(persistentVM.OSVirtualHardDisk, new Management.Compute.Models.OSVirtualHardDisk()),
                RoleName = persistentVM.RoleName,
                RoleSize = persistentVM.RoleSize,
                RoleType = persistentVM.RoleType,                
//                Label = persistentVM.Label
            };
            persistentVM.DataVirtualHardDisks.ForEach(c => result.DataVirtualHardDisks.Add(Mapper.Map(c, new Management.Compute.Models.DataVirtualHardDisk())));
            PersistentVMHelper.MapConfigurationSets(persistentVM.ConfigurationSets).ForEach(c => result.ConfigurationSets.Add(c));

            return result;
        }

        protected override void ProcessRecord()
        {
            try
            {
                this.ValidateParameters();
                base.ProcessRecord();
                this.NewAzureVMProcess();
            }
            catch (Exception ex)
            {
                WriteError(new ErrorRecord(ex, string.Empty, ErrorCategory.CloseError, null));
            }
        }

        protected void ValidateParameters()
        {
            if (ParameterSetName.Equals("CreateService", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrEmpty(Location) && string.IsNullOrEmpty(AffinityGroup))
                {
                    throw new ArgumentException(Resources.LocationOrAffinityGroupRequiredWhenCreatingNewCloudService);
                }
            }
            else
            {
                if (!string.IsNullOrEmpty(Location) && !string.IsNullOrEmpty(AffinityGroup))
                {
                    throw new ArgumentException(Resources.LocationOrAffinityGroupCanOnlyBeSpecifiedWhenNewCloudService);
                }
            }

            if (this.ParameterSetName.Equals("CreateService", StringComparison.OrdinalIgnoreCase) == true)
            {
                if (!string.IsNullOrEmpty(this.VNetName) && string.IsNullOrEmpty(this.AffinityGroup))
                {
                    throw new ArgumentException(Resources.MustSpecifySameAffinityGroupAsVirtualNetwork);
                }
            }

            if (this.ParameterSetName.Equals("CreateService", StringComparison.OrdinalIgnoreCase) == true || this.ParameterSetName.Equals("CreateDeployment", StringComparison.OrdinalIgnoreCase) == true)
            {
                if (this.DnsSettings != null && string.IsNullOrEmpty(this.VNetName))
                {
                    throw new ArgumentException(Resources.VNetNameRequiredWhenSpecifyingDNSSettings);
                }
            }

            foreach (var pVM in this.VMs)
            {
                var provisioningConfiguration = pVM.ConfigurationSets
                                    .OfType<ProvisioningConfigurationSet>()
                                    .SingleOrDefault();

                if (provisioningConfiguration == null && pVM.OSVirtualHardDisk.SourceImageName != null)
                {
                    throw new ArgumentException(string.Format(Resources.VMMissingProvisioningConfiguration, pVM.RoleName));
                }
            }
        }

        protected bool DoesCloudServiceExist(string serviceName)
        {
            try
            {
                WriteVerboseWithTimestamp(string.Format(Resources.AzureVMBeginOperation, CommandRuntime));
                var response = this.ComputeClient.HostedServices.CheckNameAvailability(serviceName);
                WriteVerboseWithTimestamp(string.Format(Resources.AzureVMCompletedOperation, CommandRuntime));
                return response.IsAvailable;
            }
            catch (CloudException ex)
            {
                if (ex.Response.StatusCode == HttpStatusCode.NotFound)
                {
                    return false;
                }
                this.WriteErrorDetails(ex);
            }

            return false;
        }
    }
}