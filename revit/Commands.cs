using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using DesignAutomationFramework;
using System;
using System.Collections.Generic;
using System.IO;

namespace UpdateFamily
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class UpdateFamilyApp : IExternalDBApplication
    {
        // Path where your updated sat file is located
        string SAT_INPUT = "InputGeometry.sat";

        // Path where your family file is located
        // ToDo: this should be an input parameter
        string RFA_TEMP = "Chair.rfa";

        // path of template file(i.e)template file where your SAT file will be imported
        string RFA_TEMPLATE = "FamilyTemplate.rft";

        // Path of the project(i.e)project where your family files are present
        string RVT_OUTPUT = "ResultModel.rvt";

        public ExternalDBApplicationResult OnStartup(Autodesk.Revit.ApplicationServices.ControlledApplication app)
        {
            DesignAutomationBridge.DesignAutomationReadyEvent += HandleDesignAutomationReadyEvent;

            return ExternalDBApplicationResult.Succeeded;
        }

        private Application _rvtApp;

        public void HandleDesignAutomationReadyEvent(object sender, DesignAutomationReadyEventArgs e)
        {
            e.Succeeded = true;

            if (e.DesignAutomationData == null) throw new ArgumentNullException(nameof(e.DesignAutomationData));

            _rvtApp = e.DesignAutomationData.RevitApp;
            if (_rvtApp == null) throw new InvalidDataException(nameof(_rvtApp));

            //Converts SAT file to family file(rfa file)
            ConvertSAT2RFA();

            //Updates the project by replacing the family file with updated family file
            UpdateRFAinProject(e.DesignAutomationData.RevitDoc);
        }

        private void ConvertSAT2RFA()
        {
            //Get the documnet from the data(i.e)templatefile document
            Document doc = _rvtApp.NewFamilyDocument(RFA_TEMPLATE);

            //check document is not null
            if (doc == null)
            {
                throw new InvalidOperationException("Could not create family.");
            }

            //Create the transaction as we are going to modify the document by importing the family file
            //Import SAT file into template file            
            using (Transaction transaction = new Transaction(doc))
            {
                //Start the transaction 
                transaction.Start("CONVERTING SAT TO RFA FILE");

                //FailureHandlingOptions will collect those warnings which occurs while importing the SAT file and deletes those warning
                //These warnings occurs because all the data from the SAT file cannot be converted to revit family file(.rfa file)
                //For simple SAT files((i.e)SAT file of a Box) these type of warnings won't occur.
                FailureHandlingOptions FH_options = transaction.GetFailureHandlingOptions();
                FH_options.SetFailuresPreprocessor(new Warning_Swallower());
                transaction.SetFailureHandlingOptions(FH_options);

                //Create SATImportOptions
                //It help you to import the .sat files
                SATImportOptions SAT_IOption = new SATImportOptions();
                SAT_IOption.VisibleLayersOnly = true;
                SAT_IOption.Placement = ImportPlacement.Centered;
                SAT_IOption.ColorMode = ImportColorMode.Preserved;
                SAT_IOption.Unit = ImportUnit.Default;

                //get the perfect view for the placement of imported SAT file(i.e REF.LEVEL)
                //Collect all the views and get the view named as "Ref. Level" where we are going to import the sat file
                FilteredElementCollector view_collector = new FilteredElementCollector(doc).OfClass(typeof(View)).WhereElementIsNotElementType();
                IList<ElementId> view_ids = view_collector.ToElementIds() as IList<ElementId>;
                View view = null;
                foreach (ElementId view_id in view_ids)
                {
                    View v = doc.GetElement(view_id) as View;
                    if (v.Name.Contains("Ref. Level"))
                    {
                        view = v;
                    }
                }

                //import the .SAT file to family template file ((i.e)Metric Generic Model.rft) 
                doc.Import(SAT_INPUT, SAT_IOption, view);

                //After importing
                //to save the changes commit the transaction
                transaction.Commit();
            }

            //save the imported .SAT file as .RFA file
            //If the family file(.rfa) with same name  already exists in the rfa_path,overwrite the already existing family file with the modified family file
            ModelPath RFAmodelPath = ModelPathUtils.ConvertUserVisiblePathToModelPath(RFA_TEMP);
            SaveAsOptions SAO = new SaveAsOptions();
            SAO.OverwriteExistingFile = true;

            //Save the familyfile
            doc.SaveAs(RFAmodelPath, SAO);

            doc.Close();
        }

        private void UpdateRFAinProject(Document doc)
        {
            //Load and update the family symbol instances present in the project
            //make changes to the documnet by modifying the family file
            //Open transaction
            using (Transaction updating_transaction = new Transaction(doc))
            {
                //Start the transaction
                updating_transaction.Start("UPDATE THE PROJECT");

                //FailureHandlingOptions will collect those warnings which occurs while importing the family file into the project and deletes those warning  
                FailureHandlingOptions FH_options = updating_transaction.GetFailureHandlingOptions();
                FH_options.SetFailuresPreprocessor(new Warning_Swallower());
                updating_transaction.SetFailureHandlingOptions(FH_options);

                //Loads the new familysymbol
                FamilyLoadOptions FLO = new FamilyLoadOptions();
                FamilySymbol new_FS = null;
                doc.LoadFamilySymbol(RFA_TEMP, "Chair", FLO, out new_FS);

                //project gets updated after loading the family file
                //Commit the transaction to save the changes
                updating_transaction.Commit();
            }

            //save the project file in the same path after updating the project file
            //If the project file(.rvt) with same name  already exists in the project path ,overwrite the already existing project file 
            ModelPath Project_model_path = ModelPathUtils.ConvertUserVisiblePathToModelPath(RVT_OUTPUT);
            SaveAsOptions SAO = new SaveAsOptions();
            SAO.OverwriteExistingFile = true;

            //Save the project file           
            doc.SaveAs(Project_model_path, SAO);

        }

        public ExternalDBApplicationResult OnShutdown(Autodesk.Revit.ApplicationServices.ControlledApplication app)
        {
            return ExternalDBApplicationResult.Succeeded;
        }
    }

    public class FamilyLoadOptions : IFamilyLoadOptions
    {
        public bool OnFamilyFound(bool familyInUse, out bool overwriteParameterValues)
        {
            overwriteParameterValues = true;
            return true;
        }
        public bool OnSharedFamilyFound(Family sharedFamily, bool familyInUse, out FamilySource source, out bool overwriteParameterValues)
        {
            overwriteParameterValues = true;
            familyInUse = true;
            source = FamilySource.Family;
            return true;
        }
    }
    public class Warning_Swallower : IFailuresPreprocessor
    {
        public FailureProcessingResult PreprocessFailures(FailuresAccessor failuresAccessor)
        {
            IList<FailureMessageAccessor> Failures = failuresAccessor.GetFailureMessages();
            foreach (FailureMessageAccessor failure in Failures)
            {
                if (failure.GetSeverity().ToString().Contains("Warning"))
                {
                    FailureDefinitionId Id = failure.GetFailureDefinitionId();
                    failuresAccessor.DeleteAllWarnings();
                }
            }
            return FailureProcessingResult.Continue;
        }
    }

}
