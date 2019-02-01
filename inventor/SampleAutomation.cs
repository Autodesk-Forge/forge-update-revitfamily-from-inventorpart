using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Inventor;
using System.Collections.Generic;
using System.Text;
using System.IO;

// Debug

namespace SATExportPlugin
{
    [ComVisible(true)]
    public class SampleAutomation
    {
        InventorServer m_inventorServer;

        public SampleAutomation(InventorServer inventorServer)
        {
            Trace.TraceInformation("Starting sample plugin.");
            m_inventorServer = inventorServer;
        }

        public void Run(Document doc)
        {

            Trace.TraceInformation("Running with no Args.");
            NameValueMap map = m_inventorServer.TransientObjects.CreateNameValueMap();
            RunWithArguments(doc, map);

        }

        public void RunWithArguments(Document doc, NameValueMap map)
        {

            StringBuilder traceInfo = new StringBuilder("RunWithArguments called with ");
            traceInfo.Append(doc.DisplayName);
            Trace.TraceInformation(map.Count.ToString());

            // values in map are keyed on _1, _2, etc
            for (int i = 0; i < map.Count; i++)
            {
                traceInfo.Append(" and ");
                traceInfo.Append(map.Value["_" + (i + 1)]);
            }

            Trace.TraceInformation(traceInfo.ToString());

            string dirPath = System.IO.Path.GetDirectoryName(doc.FullDocumentName);


            #region ExportSAT file 

            Trace.TraceInformation("Export SAT file.");
            TranslatorAddIn oSAT = null;

            foreach (ApplicationAddIn item in m_inventorServer.ApplicationAddIns)
            {

                if (item.ClassIdString == "{89162634-02B6-11D5-8E80-0010B541CD80}")
                {
                    Trace.TraceInformation("Finded the PDF addin.");
                    oSAT = (TranslatorAddIn)item;
                    break;
                }
                else { }
            }

            if (oSAT != null)
            {
                TranslationContext oContext = m_inventorServer.TransientObjects.CreateTranslationContext();
                NameValueMap oIgesMap = m_inventorServer.TransientObjects.CreateNameValueMap();

                if (oSAT.get_HasSaveCopyAsOptions(doc, oContext, oIgesMap))
                {
                    Trace.TraceInformation("SAT can be exported.");

                    Trace.TraceInformation("SAT: Set context type");
                    oContext.Type = IOMechanismEnum.kFileBrowseIOMechanism;

                    Trace.TraceInformation("SAT: create data medium");
                    DataMedium oData = m_inventorServer.TransientObjects.CreateDataMedium();

                    Trace.TraceInformation("SAT save to: " + dirPath + "\\export.sat");
                    oData.FileName = dirPath + "\\export.sat";

                    oIgesMap.set_Value("GeometryType", 1);

                    oSAT.SaveCopyAs(doc, oContext, oIgesMap, oData);
                    Trace.TraceInformation("SAT exported.");
                }

                #endregion

            }

        }

    }
}
