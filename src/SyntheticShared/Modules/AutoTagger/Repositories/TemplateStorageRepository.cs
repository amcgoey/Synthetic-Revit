using Synthetic.Modules.AutoTagger.Commands;
using Synthetic.Modules.AutoTagger.Models;
using Synthetic.Modules.AutoTagger.ViewModels;
using Synthetic.Modules.AutoTagger.Views;
using Synthetic.Modules.AutoTagger.Repositories;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using Newtonsoft.Json;

using Synthetic.Modules.RevitDOM;
using Synthetic.Infrastructure.Serialization;
using Synthetic.Modules.StandardsManagement.ViewModels;

namespace Synthetic.Modules.AutoTagger.Repositories
{
    /// <summary>
    /// Handles all CRUD operations for TagTemplates, abstracting Revit Extensible Storage and local JSON file I/O.
    /// </summary>
    public class TemplateStorageRepository
    {
        private readonly Guid _schemaGuid = new Guid("4A9119B9-7221-4A59-AC10-7B32F4D9C8A1");
        private const string SchemaName = "SyntheticAutoTagger_Templates";
        private const string FieldName = "TemplateJsonData";

        private Schema GetSchema()
        {
            Schema schema = Schema.Lookup(_schemaGuid);
            if (schema != null) return schema;

            SchemaBuilder builder = new SchemaBuilder(_schemaGuid);
            builder.SetReadAccessLevel(AccessLevel.Public);
            builder.SetWriteAccessLevel(AccessLevel.Public);
            builder.SetSchemaName(SchemaName);
            builder.AddSimpleField(FieldName, typeof(string));
            return builder.Finish();
        }

        private DataStorage? GetDataStorage(Document doc, Schema schema)
        {
            var collector = new FilteredElementCollector(doc).OfClass(typeof(DataStorage));
            foreach (DataStorage ds in collector)
            {
                Entity entity = ds.GetEntity(schema);
                if (entity.IsValid()) return ds;
            }
            return null;
        }

        /// <summary>
        /// Retrieves all templates saved within the active Revit Document.
        /// </summary>
        public List<TagTemplate> GetTemplates(Document doc)
        {
            Schema schema = GetSchema();
            DataStorage? ds = GetDataStorage(doc, schema);
            if (ds == null) return new List<TagTemplate>();
            Entity entity = ds.GetEntity(schema);
            string jsonData = entity.Get<string>(FieldName);
            if (string.IsNullOrWhiteSpace(jsonData)) return new List<TagTemplate>();
            try
            {
                return JsonConvert.DeserializeObject<List<TagTemplate>>(jsonData) ?? new List<TagTemplate>();
            }
            catch
            {
                return new List<TagTemplate>();
            }
        }

        /// <summary>
        /// Saves a template to the Revit Document. Creates the DataStorage element if it does not exist.
        /// </summary>
        public void SaveTemplate(Document doc, TagTemplate template)
        {
            List<TagTemplate> templates = GetTemplates(doc);
            
            // Update existing or add new
            var existing = templates.FirstOrDefault(t => t.Id == template.Id);
            if (existing != null) templates.Remove(existing);
            templates.Add(template);
            string jsonData = JsonConvert.SerializeObject(templates);
            Schema schema = GetSchema();
            using (Transaction t = new Transaction(doc, "Save Tag Template"))
            {
                t.Start();
                DataStorage? ds = GetDataStorage(doc, schema);
                if (ds == null)
                {
                    ds = DataStorage.Create(doc);
                }
                Entity entity = new Entity(schema);
                entity.Set(FieldName, jsonData);
                ds.SetEntity(entity);
                t.Commit();
            }
        }

        /// <summary>
        /// Exports a list of templates to a local JSON file.
        /// </summary>
        public void ExportToJson(List<TagTemplate> templates, string filePath)
        {
            string jsonData = JsonConvert.SerializeObject(templates, Formatting.Indented);
            File.WriteAllText(filePath, jsonData);
        }

        /// <summary>
        /// Imports a list of templates from a local JSON file.
        /// </summary>
        /// <param name="filePath">The file path to the JSON file containing tag templates.</param>
        /// <returns>A list of deserialized TagTemplate objects.</returns>
        public List<TagTemplate> ImportFromJson(string filePath)
        {
            if (!File.Exists(filePath)) return new List<TagTemplate>();
            string jsonData = File.ReadAllText(filePath);
            return JsonConvert.DeserializeObject<List<TagTemplate>>(jsonData) ?? new List<TagTemplate>();
        }

        /// <summary>
        /// Deletes a specific template from the Extensible Storage.
        /// </summary>
        public void DeleteTemplate(Document doc, Guid templateId)
        {
            List<TagTemplate> templates = GetTemplates(doc);
            var existing = templates.FirstOrDefault(t => t.Id == templateId);
            if (existing != null)
            {
                templates.Remove(existing);
                string jsonData = JsonConvert.SerializeObject(templates);
                Schema schema = GetSchema();

                using (Transaction t = new Transaction(doc, "Delete Tag Template"))
                {
                    t.Start();
                    DataStorage? ds = GetDataStorage(doc, schema);
                    if (ds != null)
                    {
                        Entity entity = new Entity(schema);
                        entity.Set(FieldName, jsonData);
                        ds.SetEntity(entity);
                    }
                    t.Commit();
                }
            }
        }

        /// <summary>
        /// Bulk saves templates, merging them with existing templates by ID.
        /// </summary>
        public void SaveAllTemplates(Document doc, List<TagTemplate> newTemplates)
        {
            List<TagTemplate> templates = GetTemplates(doc);
            
            foreach (var nt in newTemplates)
            {
                templates.RemoveAll(t => t.Id == nt.Id);
                templates.Add(nt);
            }

            string jsonData = JsonConvert.SerializeObject(templates);
            Schema schema = GetSchema();

            using (Transaction t = new Transaction(doc, "Import Tag Templates"))
            {
                t.Start();
                DataStorage? ds = GetDataStorage(doc, schema);
                if (ds == null) ds = DataStorage.Create(doc);

                Entity entity = new Entity(schema);
                entity.Set(FieldName, jsonData);
                ds.SetEntity(entity);
                t.Commit();
            }
        }
    }
}
