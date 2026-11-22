//
// (C) Copyright 2003-2019 by Autodesk, Inc.
//
// Permission to use, copy, modify, and distribute this software in
// object code form for any purpose and without fee is hereby granted,
// provided that the above copyright notify appears in all copies and
// that both that copyright notify and the limited warranty and
// restricted rights notify below appear in all supporting
// documentation.
//
// AUTODESK PROVIDES THIS PROGRAM "AS IS" AND WITH ALL FAULTS.
// AUTODESK SPECIFICALLY DISCLAIMS ANY IMPLIED WARRANTY OF
// MERCHANTABILITY OR FITNESS FOR A PARTICULAR USE. AUTODESK, INC.
// DOES NOT WARRANT THAT THE OPERATION OF THE PROGRAM WILL BE
// UNINTERRUPTED OR ERROR FREE.
//
// Use, duplication, or disclosure by the U.S. Government is subject to
// restrictions set forth in FAR 52.227-19 (Commercial Computer
// Software - Restricted Rights) and DFAR 252.227-7013(c)(1)(ii)
// (Rights in Technical Data and Computer Software), as applicable.
//

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Diagnostics;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.DB.ExtensibleStorage;
using Document = Autodesk.Revit.DB.Document;
using System.Windows.Forms;
using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;
using Synthetic.Modules.StandardsManagement.ViewModels;



namespace Synthetic.Shared.RevitAPI
{
    /// <summary>
    /// Utility methods for interacting with Revit Extensible Storage.
    /// </summary>
    public class StorageUtil
    {
        /// <summary>
        /// Returns true if any extensible storage exists in the document, false otherwise.
        /// </summary>
        public static bool DoesAnyStorageExist(Document doc)
        {
            IList<Schema> schemas = Schema.ListSchemas();
            if (schemas.Count == 0)
                return false;
            else
            {
                foreach (Schema schema in schemas)
                {
                    List<ElementId> ids = ElementsWithStorage(doc, schema);
                    if (ids.Count > 0)
                        return true;
                }
                return false;
            }
        }

        /// <summary>
        /// Purges the given list of Schema from all open documents.
        /// </summary>
        /// <param name="schemas">List of schemas</param>
        /// <param name="document">Document to perform the transaction in.</param>
        public static List<string>? PurgeSchema(List<Schema> schemas, Document document)
        {
            List<string> results = new List<string>();
            int appVersion = int.Parse(document.Application.VersionNumber);
            string transactionName = "Erase Extensible Storage";
            using (Autodesk.Revit.DB.Transaction trans = new Autodesk.Revit.DB.Transaction(document))
            {
                trans.Start(transactionName);
                try
                {
                    foreach (Schema schema in schemas)
                    {
#if REVIT2022
                        // Schema.ErasSchemaAndAllEntities deprecated from Revit API in Revit 2023 and later
                        if (appVersion < 2023)
                        {
                            Schema.EraseSchemaAndAllEntities(schema, true);
                            results.Add(schema.SchemaName);
                        }
#endif
                        //Note-this will delete storage of this schema in *all* open documents.
                        if (schema.WriteAccessGranted())
                        {
                            document.EraseSchemaAndAllEntities(schema);
                            results.Add(schema.SchemaName);
                        }
                    }
                    trans.Commit();
                }
                catch { trans.RollBack(); }
            }
            if (results.Count > 0) { return results; }
            else { return null; }
        }

        /// <summary>
        /// Gets the schemas that have elements in the given document.
        /// </summary>
        /// <param name="doc">An Autodesk Revit Document obejct</param>
        /// <returns>List of schemas in the document.</returns>
        public static List<Schema>? GetDocumentSchemas(Document doc)
        {
            IList<Schema> schemas = Schema.ListSchemas();
            List<Schema> docSchemas = new List<Schema>();
            if (schemas.Count == 0)
                return null;
            else
            {
                foreach (Schema schema in schemas)
                {
                    List<ElementId> ids = ElementsWithStorage(doc, schema);
                    if(ids.Count > 0)
                    {
                        docSchemas.Add(schema);
                    }
                }
                if (schemas.Count > 0)
                {
                    return docSchemas;
                }
                else return null;
            }
        }

        /// <summary>
        /// Returns a formatted string containing schema guids and element info for all elements
        /// containing extensible storage.
        /// </summary>
        public static string GetElementStringWithAllSchemas(Document doc)
        {
            StringBuilder sBuilder = new StringBuilder();
            IList<Schema> schemas = Schema.ListSchemas();
            if (schemas.Count == 0)
                return "No schemas or storage.";
            else
            {
                foreach (Schema schema in schemas)
                {
                    sBuilder.Append(StorageUtil.GetElementStringWithSchema(doc, schema));
                }
                return sBuilder.ToString();
            }
        }

        /// <summary>
        /// Returns a formatted string containing a schema guid and element info for all elements
        /// containing extensible storage of a given schema.
        /// </summary>
        private static string GetElementStringWithSchema(Document doc, Schema schema)
        {
            StringBuilder sBuilder = new StringBuilder();
            sBuilder.AppendLine("Schema: " + schema.GUID.ToString() + ", " + schema.SchemaName);
            List<ElementId> elementsofSchema = ElementsWithStorage(doc, schema);
            if (elementsofSchema.Count == 0)
                sBuilder.AppendLine("No elements.");
            else
            {
                foreach (ElementId id in elementsofSchema)
                {
                    sBuilder.AppendLine(PrintElementInfo(id, doc));
                }
            }
            return sBuilder.ToString();
        }

        /// <summary>
        /// Returns a list of ElementIds that contain extensible storage of a given schema using
        /// the ExtensibleStorageFilter ElementQuickFilter.
        /// </summary>
        private static List<ElementId> ElementsWithStorage(Document doc, Schema schema)
        {
            List<ElementId> ids = new List<ElementId>();
            FilteredElementCollector collector = new FilteredElementCollector(doc);
            collector.WherePasses(new ExtensibleStorageFilter(schema.GUID));
            ids.AddRange(collector.ToElementIds());
            return ids;
        }



        /// <summary>
        /// Writes basic element info to a string.
        /// </summary>
        private static string PrintElementInfo(ElementId id, Document document)
        {
            Element element = document.GetElement(id);
            string retval = (element.Id.ToString() + ", " + element.Name + ", " + element.GetType().FullName);
            Debug.WriteLine(retval);
            return retval;
        }

    }
}
