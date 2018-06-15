using Sitecore.ContentSearch.Azure.Analyzers;
using Sitecore.ContentSearch.Azure.Http;
using Sitecore.ContentSearch.Azure.Models;
using Sitecore.ContentSearch.Azure.Utils.Retryer;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml;
using YourProjectName.Foundation.Indexing.Models.AzureSearchModels;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Sitecore.Configuration;
using Sitecore.ContentSearch.Azure.Schema;
using Sitecore.Xml;

namespace YourProjectName.Foundation.Indexing.Services.AzureSearch
{
    /// <summary>
    /// This class is created to expand sitecore posibility to add suggestiond to Azure search index
    /// In order to use this class you have to create custom search indexes
    /// Your search index should have node suggesters with attributes name and searchMode
    /// eg. <suggesters name="Suggester" searchMode="analyzingInfixMatching" />
    /// You need to create childreen node <sourceFields /> with another children node with attribute fieldName <suggesterField suggesterFieldName="content_1" />
    /// there you specify which fields will be added to suggester
    /// In order to get this to work you should patch schemaSynchronizer type within your custom index 
    /// type="YourProjectName.Foundation.Indexing.Services.AzureSearch.CloudSearchServiceSchemaSynchronizer, YourProjectName.Foundation.Indexing" patch:instead="schemaSynchronizer[@type='Sitecore.ContentSearch.Azure.Schema.SearchServiceSchemaSynchronizer, Sitecore.ContentSearch.Azure']" />
    /// Take everythig in the node schemaSynchronizer from Sitecore.ContentSearch.Azure.DefaultIndexConfiguration.config
    /// </summary>
    public class CloudSearchServiceSchemaSynchronizer : SearchServiceSchemaSynchronizer
    {
        public new void Initialize(string indexName, string connectionString)
        {
            ISearchServiceConnectionInitializable managmentOperations = this.ManagmentOperations as ISearchServiceConnectionInitializable;
            if (managmentOperations == null)
                return;
            managmentOperations.Initialize(indexName, connectionString);
        }
        protected override IndexDefinition SyncRemoteService(IndexDefinition sourceIndexDefinition, IEnumerable<IndexedField> incomingFields)
        {
            IEnumerable<IndexedField> mainFields = sourceIndexDefinition?.Fields ?? (IEnumerable<IndexedField>)new List<IndexedField>();
            incomingFields = incomingFields ?? (IEnumerable<IndexedField>)new List<IndexedField>();
            bool isModified1;
            IEnumerable<IndexedField> indexedFields = this.MergeFields(mainFields, incomingFields, out isModified1);
            if (!isModified1 && sourceIndexDefinition != null)
                return new IndexDefinition(sourceIndexDefinition.AnalyzerDefinitions, indexedFields);
            var index = this.ManagmentOperations.GetIndex();
            if (index == null)
            {
                IndexDefinition indexDefinition = new CloudIndexDefinition(this.AnalyzerRepository.GetAnalyzers(), indexedFields);
                this.ManagmentOperations.CreateIndex(indexDefinition);
                return indexDefinition;
            }
            bool isModified2;
            IEnumerable<IndexedField> fields = this.MergeFields(index.Fields, indexedFields, out isModified2);
            var indexDefinition1 = new CloudIndexDefinition(index.AnalyzerDefinitions, fields);
            if (isModified2)
                this.ManagmentOperations.UpdateIndex(indexDefinition1);
            return indexDefinition1;
        }

        [Obsolete]
        public CloudSearchServiceSchemaSynchronizer(ISearchServiceManagmentOperationsProvider managmentOperations, IRertyPolicy rertyPolicy) : base(managmentOperations, rertyPolicy)
        {
        }

        public CloudSearchServiceSchemaSynchronizer(ISearchServiceManagmentOperationsProvider managmentOperations, IRertyPolicy rertyPolicy, IAnalyzerRepository analyzerRepository) : base(managmentOperations, rertyPolicy, analyzerRepository)
        {
        }
    }

    [JsonConverter(typeof(CloudIndexDefinitionJsonConverter))]
    public class CloudIndexDefinition : IndexDefinition
    {
        public CloudIndexDefinition(AnalyzerDefinitions analyzerDefinitions, IEnumerable<IndexedField> fields) : base(analyzerDefinitions, fields)
        {
        }
    }
    /// <summary>
    /// Here is where it starts to search for suggester
    /// </summary>
    public class CloudIndexDefinitionJsonConverter : IndexDefinitionJsonConverter
    {
        private List<string> _sugesterFields;
        private string _suggesterName;
        private string _suggesterSearchMode;

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            var suggester = Factory.GetConfigNode("suggesters");
            if (suggester?.Attributes == null)
                return;
            _suggesterName = suggester.Attributes["name"].Value;

            if (!_suggesterName.Any())
                return;
            _suggesterSearchMode = suggester.Attributes["searchMode"].Value;

            _sugesterFields =
                (Factory.GetConfigNodes("suggesters/sourceFields/suggesterField")
                    .Cast<XmlNode>()
                    .Select(node => XmlUtil.GetAttribute("suggesterFieldName", node))).ToList();

            if (!_sugesterFields.Any())
                return;

            var suggesters = new List<Suggester>
            {
                new Suggester(_suggesterName, _suggesterSearchMode, _sugesterFields)
            };

            IEnumerable<JToken> suggjtokens = suggesters.Select(f => JToken.FromObject((object)f, serializer));

            IndexDefinition indexDefinition = value as IndexDefinition;
            if (indexDefinition == null)
                return;
            JObject jobject = JObject.FromObject((object)indexDefinition.AnalyzerDefinitions, serializer) ?? new JObject();
            IEnumerable<JToken> jtokens = indexDefinition.Fields.Select<IndexedField, JToken>((Func<IndexedField, JToken>)(f => JToken.FromObject((object)f, serializer)));
            jobject.AddFirst((object)new JProperty("fields", (object)new JArray((object)jtokens)));
            jobject.AddFirst((object)new JProperty("suggesters", (object)new JArray((object)suggjtokens)));
            jobject.WriteTo(writer, Array.Empty<JsonConverter>());
        }
    }
}
