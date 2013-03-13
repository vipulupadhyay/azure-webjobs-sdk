﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Mvc;
using DaasEndpoints;
using Executor;
using Microsoft.WindowsAzure;
using Microsoft.WindowsAzure.StorageClient;
using Orchestrator;
using RunnerInterfaces;
using SimpleBatch;

namespace WebFrontEnd.Controllers
{
    [Authorize]
    public class HomeController : Controller
    {
        private static Services GetServices()
        {
            AzureRoleAccountInfo accountInfo = new AzureRoleAccountInfo();
            return new Services(accountInfo);
        }

        //
        // GET: /Home/

        // Like the homepage
        public ActionResult Index()
        {
            OverviewModel model = new OverviewModel();
            
            // Get health 
            var services = GetServices();
            model.QueueDepth = services.GetExecutionQueueDepth();
            model.HealthStatus = services.GetHealthStatus();
            model.AccountName = services.Account.Credentials.AccountName;
            
            return View(model);
        }

        public ActionResult ListAllFunctions()
        {
            var model = new FunctionListModel
            {
                Functions = GetServices().GetFunctionTable().ReadAll()
            };

            return View(model);
        }


        public ActionResult ListAllBinders()
        {
            var binderLookupTable = GetServices().GetBinderTable();

            var x = from kv in binderLookupTable.EnumerateDict() select new BinderListModel.Entry 
            {
                 AccountName = Utility.GetAccountName(kv.Value.AccountConnectionString),
                 TypeName = kv.Key.Item2,
                 Path = kv.Value.Path,
                 EntryPoint = string.Format("{0}!{1}", kv.Value.InitAssembly, kv.Value.InitType)
            };

            var model = new BinderListModel
            {
                Binders = x.ToArray()
            };

            return View(model);
        }

        // Scan a blobpath for new blobs. This will queue to the execution
        // Useful when there are paths that are not being listened on
        public ActionResult RequestScan()
        {
            var model = new FunctionListModel
            {
                Functions = GetServices().GetFunctionTable().ReadAll()
            };
            return View(model);
        }

        public static CloudStorageAccount GetAccount(string AccountName, string AccountKey)
        {
            // $$$ StorageAccounts are more than just Name,Key. 
            // So special case getting the dev store account. 
            if (AccountName == "devstoreaccount1")
            {
                return CloudStorageAccount.DevelopmentStorageAccount;
            }
            return new CloudStorageAccount(new StorageCredentialsAccountAndKey(AccountName, AccountKey), false);
        }

        // Scan a container and queue execution items.
        [HttpPost]
        public ActionResult RequestScanSubmit(FunctionIndexEntity function, string accountname, string accountkey, CloudBlobPath containerpath)
        {
            CloudStorageAccount account;
            if (function != null)
            {
                account = function.GetAccount();
            }
            else
            {
                account = GetAccount(accountname, accountkey);
            }
            int count = Helpers.ScanBlobDir(GetServices(), account, containerpath);

            RequestScanSubmitModel model = new RequestScanSubmitModel();
            model.CountScanned = count;
            return View(model);
        }

        public ActionResult RegisterFunc()
        {
            return View();
        }

        // Try to lookup the connection string (including key!) for a given account. 
        // This works for accounts that are already registered with the service.
        // Return null if not found.
        internal static string TryLookupConnectionString(string accountName)
        {
            // If account key is blank, see if we can look it up
            var funcs = GetServices().GetFunctionTable().ReadAll();
            foreach (var func in funcs)
            {
                var cred = func.GetAccount().Credentials;

                if (string.Compare(cred.AccountName, accountName, ignoreCase: true) == 0)
                {
                    return func.Location.Blob.AccountConnectionString;
                }
            }

            // not found
            return null;            
        }

        public ActionResult RegisterFuncSubmit(string AccountName, string AccountKey, string ContainerName)
        {
            // Check for typos upfront.

            string accountConnectionString = null;

            // If account key is blank, see if we can look it up
            if (string.IsNullOrWhiteSpace(AccountKey))
            {
                accountConnectionString = TryLookupConnectionString(AccountName);                   
            }

            if (accountConnectionString == null)
            {
                accountConnectionString  = Utility.GetConnectionString(GetAccount(AccountName, AccountKey));
            }

            return RegisterFuncSubmitworker(accountConnectionString, ContainerName);
        }


        [HttpPost]
        public ActionResult RescanFunction(FunctionIndexEntity func)
        {
            return RegisterFuncSubmitworker(func.Location.Blob.AccountConnectionString, func.Location.Blob.ContainerName);
        }

        private ActionResult RegisterFuncSubmitworker(string accountConnectionString, string ContainerName)
        {
            var model = ExecutionController.RegisterFuncSubmitworker(accountConnectionString, ContainerName);

            return View("RegisterFuncSubmit", model);
        }             
    }
}