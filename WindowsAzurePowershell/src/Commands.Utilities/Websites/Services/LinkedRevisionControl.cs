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

namespace Microsoft.WindowsAzure.Commands.Utilities.Websites.Services
{
    using System;
    using System.IO;
    using System.Linq;
    using WebEntities;

    public abstract class LinkedRevisionControl : IDisposable
    {
        protected string InvocationPath;
        public abstract void Init();
        public abstract void Deploy(Site siteData);

        internal bool IsGitWorkingTree()
        {
            return Git.GetWorkingTree().Any(line => line.Equals(".git"));
        }

        internal void InitGitOnCurrentDirectory()
        {
            Git.InitRepository();

            if (!File.Exists(".gitignore"))
            {
                // Scaffold gitignore
                string cmdletPath = Directory.GetParent(InvocationPath).FullName;
                File.Copy(Path.Combine(cmdletPath, "Scaffolding/Node/Website/.gitignore"), ".gitignore");
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
        }
    }
}
