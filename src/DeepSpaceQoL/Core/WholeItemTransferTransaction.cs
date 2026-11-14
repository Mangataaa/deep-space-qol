using System;
using System.Collections.Generic;

namespace DeepSpaceQoL.Core;

/// <summary>Runs a same-frame group of real transfers; recovery detaches every changed item
/// before restoring any original location, so swapped slots never obstruct one another.</summary>
internal static class WholeItemTransferTransaction
{
    internal sealed class Result
    {
        internal bool Succeeded;
        internal bool Restored;
        internal Exception? Error;
        internal Exception? RestoreError;
    }

    internal static Result Run<T>(IReadOnlyList<T> snapshots, Action apply,
        Action<T> detach, Action<T> restore, Action verifyRestore)
    {
        try { apply(); return new Result { Succeeded = true }; }
        catch (Exception error)
        {
            try
            {
                foreach (var snapshot in snapshots) detach(snapshot);
                foreach (var snapshot in snapshots) restore(snapshot);
                verifyRestore();
                return new Result { Error = error, Restored = true };
            }
            catch (Exception restoreError) { return new Result { Error = error, RestoreError = restoreError }; }
        }
    }
}
