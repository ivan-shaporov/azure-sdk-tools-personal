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

namespace Microsoft.WindowsAzure.Commands.ServiceManagement.IaaS
{
    using System;
    using System.Management.Automation;
    using AutoMapper;
    using Commands.Utilities.Common;
    using Management.Compute;
    using Management.Compute.Models;
    using Storage;
    using Model;
    using Properties;

    [Cmdlet(VerbsData.Update, "AzureVM"), OutputType(typeof(ManagementOperationContext))]
    public class UpdateAzureVMCommand : IaaSDeploymentManagementCmdletBase
    {
        [Parameter(Position = 1, Mandatory = true, ValueFromPipelineByPropertyName = true, HelpMessage = "The name of the Virtual Machine to update.")]
        [ValidateNotNullOrEmpty]
        public string Name
        {
            get;
            set;
        }

        [Parameter(Mandatory = true, ValueFromPipeline = true, ValueFromPipelineByPropertyName = true, HelpMessage = "Virtual Machine to update.")]
        [ValidateNotNullOrEmpty]
        [Alias("InputObject")]
        public PersistentVM VM
        {
            get;
            set;
        }

        internal override void ExecuteCommand()
        {
            Mapper.Initialize(m => m.AddProfile<ServiceManagementProfile>());

            base.ExecuteCommand();

            SubscriptionData currentSubscription = this.GetCurrentSubscription();
            if (CurrentDeploymentNewSM == null)
            {
                return;
            }

            // Auto generate disk names based off of default storage account 
            foreach (var datadisk in this.VM.DataVirtualHardDisks)
            {
                if (datadisk.MediaLink == null && string.IsNullOrEmpty(datadisk.DiskName))
                {
                    CloudStorageAccount currentStorage = currentSubscription.GetCurrentStorageAccount();
                    if (currentStorage == null)
                    {
                        throw new ArgumentException(Resources.CurrentStorageAccountIsNotAccessible);
                    }

                    DateTime dateTimeCreated = DateTime.Now;
                    string diskPartName = VM.RoleName;

                    if (datadisk.DiskLabel != null)
                    {
                        diskPartName += "-" + datadisk.DiskLabel;
                    }

                    string vhdname = string.Format("{0}-{1}-{2}-{3}-{4}-{5}.vhd", ServiceName, diskPartName, dateTimeCreated.Year, dateTimeCreated.Month, dateTimeCreated.Day, dateTimeCreated.Millisecond);
                    string blobEndpoint = currentStorage.BlobEndpoint.AbsoluteUri;

                    if (blobEndpoint.EndsWith("/") == false)
                    {
                        blobEndpoint += "/";
                    }

                    datadisk.MediaLink = new Uri(blobEndpoint + "vhds/" + vhdname);
                }

                if (VM.DataVirtualHardDisks.Count > 1)
                {
                    // To avoid duplicate disk names
                    System.Threading.Thread.Sleep(1);
                }
            }

            VirtualMachineRoleSize roleSizeResult;
            if (!Enum.TryParse(VM.RoleSize, true, out roleSizeResult))
            {
                throw new ArgumentOutOfRangeException("RoleSize:" + VM.RoleSize);
            }

            //TODO: https://github.com/WindowsAzure/azure-sdk-for-net-pr/issues/130
            //TODO: https://github.com/WindowsAzure/azure-sdk-for-net-pr/issues/136
            var parameters = new VirtualMachineUpdateParameters
            {
                AvailabilitySetName = VM.AvailabilitySetName,
//                Label = VM.Label,
                OSVirtualHardDisk = Mapper.Map(VM.OSVirtualHardDisk, new OSVirtualHardDisk()),
                RoleName = VM.RoleName,
                RoleSize = roleSizeResult,
//                RoleType = VM.RoleType
            };
            VM.DataVirtualHardDisks.ForEach(c => parameters.DataVirtualHardDisks.Add(Mapper.Map(c, new DataVirtualHardDisk())));
            VM.ConfigurationSets.ForEach(c => parameters.ConfigurationSets.Add(Mapper.Map(c, new ConfigurationSet())));

            ExecuteClientActionNewSM(
                parameters,
                CommandRuntime.ToString(),
                () => this.ComputeClient.VirtualMachines.Update(this.ServiceName, CurrentDeploymentNewSM.Name, this.Name, parameters),
                (s, response) => ContextFactory<ComputeOperationStatusResponse, ManagementOperationContext>(response, s));
        }
    }
}