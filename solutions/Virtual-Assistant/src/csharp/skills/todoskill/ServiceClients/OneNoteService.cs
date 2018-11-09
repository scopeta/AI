﻿// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

namespace ToDoSkill
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net.Http;
    using System.Threading.Tasks;
    using System.Xml;
    using Microsoft.Graph;
    using Newtonsoft.Json;
    using Newtonsoft.Json.Linq;
    using static global::ToDoSkill.ListTypes;

    /// <summary>
    /// To Do bot service.
    /// </summary>
    public class OneNoteService : ITaskService
    {
        private const string OneNoteNotebookName = "ToDoNotebook";
        private const string OneNoteSectionName = "ToDoSection";
        private readonly string graphBaseUrl = "https://graph.microsoft.com/v1.0/me";
        private string toDoPageName = ListType.ToDo.ToString();
        private string groceryPageName = ListType.Grocery.ToString();
        private string shoppingPageName = ListType.Shopping.ToString();
        private HttpClient httpClient;
        private Dictionary<string, string> pageIds;

        /// <summary>
        /// Initializes OneNote task service using token.
        /// </summary>
        /// <param name="token">the token used for msgraph API call.</param>
        /// <param name="pageIds">the page ids.</param>
        /// <returns>OneNote task service itself.</returns>
        public async Task<ITaskService> InitAsync(string token, Dictionary<string, string> pageIds, HttpClient client = null)
        {
            try
            {
                if (client == null)
                {
                    httpClient = ServiceHelper.GetHttpClient(token);
                }
                else
                {
                    httpClient = client;
                }

                if (!pageIds.ContainsKey(toDoPageName)
                    || !pageIds.ContainsKey(groceryPageName)
                    || !pageIds.ContainsKey(shoppingPageName))
                {
                    var notebookId = await GetOrCreateNotebookAsync(OneNoteNotebookName);
                    var sectionId = await GetOrCreateSectionAsync(notebookId, OneNoteSectionName);

                    if (!pageIds.ContainsKey(toDoPageName))
                    {
                        var toDoPageId = await GetOrCreatePageAsync(sectionId, toDoPageName);
                        pageIds.Add(toDoPageName, toDoPageId);
                    }

                    if (!pageIds.ContainsKey(groceryPageName))
                    {
                        var groceryPageId = await GetOrCreatePageAsync(sectionId, groceryPageName);
                        pageIds.Add(groceryPageName, groceryPageId);
                    }

                    if (!pageIds.ContainsKey(shoppingPageName))
                    {
                        var shoppingPageId = await GetOrCreatePageAsync(sectionId, shoppingPageName);
                        pageIds.Add(shoppingPageName, shoppingPageId);
                    }
                }

                this.pageIds = pageIds;
                return this;
            }
            catch (Exception ex)
            {
                throw ex;
            }
        }

        /// <summary>
        /// Get To Do tasks.
        /// </summary>
        /// <param name="listType">Task list type.</param>
        /// <returns>List of task items.</returns>
        public async Task<List<TaskItem>> GetTasksAsync(string listType)
        {
            try
            {
                var pages = await GetOneNotePageByIdAsync(pageIds[listType]);

                var retryCount = 2;
                while ((pages == null || pages.Count == 0) && retryCount > 0)
                {
                    pages = await GetOneNotePageByIdAsync(pageIds[listType]);
                    retryCount--;
                }

                if (pages != null && pages.Count > 0)
                {
                    var todos = await GetToDoContentAsync(pages.First().ContentUrl);
                    return todos;
                }
                else
                {
                    throw new Exception("Can not get the To Do OneNote pages.");
                }
            }
            catch (Exception ex)
            {
                throw ex;
            }
        }

        /// <summary>
        /// Add a task.
        /// </summary>
        /// <param name="listType">Task list type.</param>
        /// <param name="taskText">The task text.</param>
        /// <returns>Ture if succeed.</returns>
        public async Task<bool> AddTaskAsync(string listType, string taskText)
        {
            var pageContentUrl = await this.GetDefaultToDoPageAsync(listType);
            var todoContent = await httpClient.GetStringAsync(pageContentUrl.ContentUrl + "/?includeIDs=true");
            var httpRequestMessage = ServiceHelper.GenerateAddToDoHttpRequest(taskText, todoContent, pageContentUrl.ContentUrl);
            var result = await httpClient.SendAsync(httpRequestMessage);
            return result.IsSuccessStatusCode;
        }

        /// <summary>
        /// Mark tasks as completed.
        /// </summary>
        /// <param name="listType">Task list type.</param>
        /// <param name="taskItems">Task items.</param>
        /// <returns>True if succeed.</returns>
        public async Task<bool> MarkTasksCompletedAsync(string listType, List<TaskItem> taskItems)
        {
            var pageContentUrl = await this.GetDefaultToDoPageAsync(listType);
            var httpRequestMessage = ServiceHelper.GenerateMarkToDosHttpRequest(taskItems, pageContentUrl.ContentUrl);
            var result = await httpClient.SendAsync(httpRequestMessage);
            return result.IsSuccessStatusCode;
        }

        /// <summary>
        /// Delete tasks.
        /// </summary>
        /// <param name="listType">Task list type.</param>
        /// <param name="taskItems">Task items.</param>
        /// <returns>True if succeed.</returns>
        public async Task<bool> DeleteTasksAsync(string listType, List<TaskItem> taskItems)
        {
            var pageContentUrl = await this.GetDefaultToDoPageAsync(listType);
            var httpRequestMessage = ServiceHelper.GenerateDeleteToDosHttpRequest(taskItems, pageContentUrl.ContentUrl);
            var result = await httpClient.SendAsync(httpRequestMessage);
            return result.IsSuccessStatusCode;
        }

        private async Task<string> CreateOneNoteNotebookAsync(string createNotebookUrl, string notebookName)
        {
            var makeSectionContent = await httpClient.GetStringAsync(createNotebookUrl);
            var httpRequestMessage = ServiceHelper.GenerateCreateNotebookHttpRequest(makeSectionContent, createNotebookUrl, notebookName);
            var result = await httpClient.SendAsync(httpRequestMessage);
            dynamic responseContent = JObject.Parse(await result.Content.ReadAsStringAsync());
            return (string)responseContent.id;
        }

        private async Task<string> GetOrCreateNotebookAsync(string notebookName)
        {
            var notebooksUrl = $"{graphBaseUrl}/onenote/notebooks";
            var onenoteNotebook = await GetOneNoteNotebookAsync($"{notebooksUrl}?filter=name eq '{notebookName}'");
            if (onenoteNotebook.Count == 0)
            {
                return await CreateOneNoteNotebookAsync(notebooksUrl, notebookName);
            }

            return onenoteNotebook[0].Id;
        }

        private async Task<List<Notebook>> GetOneNoteNotebookAsync(string url)
        {
            return JsonConvert.DeserializeObject<List<Notebook>>(await ExecuteGraphFetchAsync(url));
        }

        private async Task<string> CreateOneNoteSectionAsync(string sectionContentUrl, string sectionTitle)
        {
            var makeSectionContent = await httpClient.GetStringAsync(sectionContentUrl);
            var httpRequestMessage = ServiceHelper.GenerateCreateSectionHttpRequest(makeSectionContent, sectionContentUrl, sectionTitle);
            var result = await httpClient.SendAsync(httpRequestMessage);
            dynamic responseContent = JObject.Parse(await result.Content.ReadAsStringAsync());
            return (string)responseContent.id;
        }

        private async Task<string> GetOrCreateSectionAsync(string notebookId, string sectionTitle)
        {
            var sectionsUrl = $"{graphBaseUrl}/onenote/notebooks/{notebookId}/sections";
            var onenoteSection = await GetOneNoteSectionAsync($"{sectionsUrl}?filter=name eq '{sectionTitle}'");
            if (onenoteSection.Count == 0)
            {
                return await CreateOneNoteSectionAsync(sectionsUrl, sectionTitle);
            }

            return onenoteSection[0].Id;
        }

        private async Task<List<OnenoteSection>> GetOneNoteSectionAsync(string url)
        {
            return JsonConvert.DeserializeObject<List<OnenoteSection>>(await ExecuteGraphFetchAsync(url));
        }

        private async Task<bool> CreateOneNotePageAsync(string sectionUrl, string pageTitle)
        {
            var httpRequestMessage = ServiceHelper.GenerateCreatePageHttpRequest(pageTitle, sectionUrl);
            var result = await httpClient.SendAsync(httpRequestMessage);
            return result.IsSuccessStatusCode;
        }

        private async Task<string> GetOrCreatePageAsync(string sectionId, string pageTitle)
        {
            var pagesUrl = $"{graphBaseUrl}/onenote/sections/{sectionId}/pages";
            var onenotePage = await GetOneNotePageAsync($"{pagesUrl}?filter=title eq '{pageTitle}'");
            if (onenotePage == null || onenotePage.Count == 0)
            {
                var successFlag = await CreateOneNotePageAsync(pagesUrl, pageTitle);
                if (successFlag)
                {
                    var retryCount = 3;
                    while ((onenotePage == null || onenotePage.Count == 0) && retryCount > 0)
                    {
                        onenotePage = await GetOneNotePageAsync($"{pagesUrl}?filter=title eq '{pageTitle}'");
                        retryCount--;
                    }
                }
                else
                {
                    throw new Exception("Can not create the To Do OneNote page.");
                }
            }

            if (onenotePage == null || onenotePage.Count == 0)
            {
                throw new Exception("Can not get the To Do OneNote page.");
            }
            else
            {
                return onenotePage[0].Id;
            }
        }

        private async Task<List<OnenotePage>> GetOneNotePageAsync(string url)
        {
            return JsonConvert.DeserializeObject<List<OnenotePage>>(await ExecuteGraphFetchAsync(url));
        }

        private async Task<List<OnenotePage>> GetOneNotePageByIdAsync(string pageId)
        {
            var pageByIdUrl = $"{graphBaseUrl}/onenote/pages?filter=id eq '{pageId}'";
            return await GetOneNotePageAsync(pageByIdUrl);
        }

        private async Task<List<TaskItem>> GetToDoContentAsync(string pageContentUrl)
        {
            var todoContent = await httpClient.GetStringAsync(pageContentUrl + "?includeIDs=true");
            var doc = new XmlDocument();
            doc.LoadXml(todoContent);
            XmlNode root = doc.DocumentElement;

            var todosList = root.SelectSingleNode("body")
                ?.SelectSingleNode("div")
                ?.SelectNodes("p")
                ?.Cast<XmlNode>()
                ?.Where(node => node.Attributes["data-tag"] != null && node.Attributes["data-tag"].Value.StartsWith("to-do"))
                ?.Select(node => new TaskItem() { Topic = node.InnerText, Id = node.Attributes["id"].Value, IsCompleted = node.Attributes["data-tag"].Value == "to-do" ? false : true })
                ?.ToList();

            if (todosList == null)
            {
                todosList = new List<TaskItem>();
            }

            return todosList;
        }

        private async Task<OnenotePage> GetDefaultToDoPageAsync(string listType)
        {
            try
            {
                var pages = await GetOneNotePageByIdAsync(pageIds[listType]);

                var retryCount = 2;
                while ((pages == null || pages.Count == 0) && retryCount > 0)
                {
                    pages = await GetOneNotePageByIdAsync(pageIds[listType]);
                    retryCount--;
                }

                if (pages != null && pages.Count > 0)
                {
                    return pages.First();
                }
                else
                {
                    throw new Exception("Can not get the To Do OneNote pages.");
                }
            }
            catch (Exception ex)
            {
                throw ex;
            }
        }

        private async Task<string> ExecuteGraphFetchAsync(string url)
        {
            var result = await httpClient.GetStringAsync(url);
            dynamic content = JObject.Parse(result);
            return JsonConvert.SerializeObject((object)content.value);
        }
    }
}
