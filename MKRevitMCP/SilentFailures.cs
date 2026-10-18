using Autodesk.Revit.DB;

namespace MKRevitMCP
{
    // Revit can raise warnings while committing a transaction, and by default it shows
    // them in a modal dialog. A dialog blocks Revit's idle loop, which freezes the MCP
    // server until someone clicks it. This handler dismisses warnings silently and
    // rolls the transaction back if there are real errors.
    public class SilentFailures : IFailuresPreprocessor
    {
        public FailureProcessingResult PreprocessFailures(FailuresAccessor accessor)
        {
            bool hasErrors = false;

            foreach (var failure in accessor.GetFailureMessages())
            {
                if (failure.GetSeverity() == FailureSeverity.Warning)
                    accessor.DeleteWarning(failure);
                else
                    hasErrors = true;
            }

            return hasErrors
                ? FailureProcessingResult.ProceedWithRollBack
                : FailureProcessingResult.Continue;
        }

        // Call after tx.Start(), before making changes.
        public static void Apply(Transaction tx)
        {
            var options = tx.GetFailureHandlingOptions();
            options.SetFailuresPreprocessor(new SilentFailures());
            options.SetClearAfterRollback(true);
            tx.SetFailureHandlingOptions(options);
        }
    }
}
