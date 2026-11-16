using System;

namespace DeepSpaceQoL.Core;

/// <summary>One same-frame, single-item production. A failed item is removed before its input is restored.</summary>
public static class ChemBatchTransaction
{
    public enum Result { Produced, NoSpace, PlacementFailed }

    public static Result One<T>(Func<T> create, Action<T> preview, Func<T, bool> canFit,
        Action checkpoint, Action<T> consumeAndMake, Func<T, bool> place, Action restore, Action<T> discard)
        where T : class
    {
        T item = create();
        bool touched = false, committed = false;
        try
        {
            preview(item);
            if (!canFit(item)) return Result.NoSpace;
            checkpoint();
            // Set this first: a native liquid callback may throw after only part of its tags were changed.
            touched = true;
            consumeAndMake(item);
            if (!canFit(item)) return Result.NoSpace;
            if (!place(item)) return Result.PlacementFailed;
            committed = true;
            return Result.Produced;
        }
        finally
        {
            if (!committed)
            {
                // If discarding fails, do not restore input and risk duplicating a surviving product.
                discard(item);
                if (touched) restore();
            }
        }
    }
}
