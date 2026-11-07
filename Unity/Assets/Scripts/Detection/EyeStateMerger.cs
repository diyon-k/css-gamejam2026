using System.Collections.Generic;

namespace Blink.Detection
{
    /// <summary>What every eye-state source says together, for one frame.</summary>
    public readonly struct EyeReading
    {
        /// <summary>At least one active source sees the eyes closed.</summary>
        public readonly bool Closed;

        /// <summary>
        /// The eyes stopped counting as closed because the source that saw them closed lost the face (or went
        /// quiet), not because it saw them open. <see cref="BlinkClassifier.LoseFace"/> handles this.
        /// </summary>
        public readonly bool FaceLost;

        /// <summary>The source reporting closed eyes, else the first active one, else "none".</summary>
        public readonly string SourceName;

        public EyeReading(bool closed, bool faceLost, string sourceName)
        {
            Closed = closed;
            FaceLost = faceLost;
            SourceName = sourceName;
        }
    }

    /// <summary>
    /// Merges <see cref="IEyeStateSource"/>s: the eyes count as closed if any active source with a face in view
    /// says so. By default a missing face counts as eyes open (looking away does nothing). It remembers which
    /// source closed the eyes, so it can tell "they opened" from "that source lost the face" even with several
    /// sources (one losing the face doesn't cancel another's blink). Pure logic, unit tested.
    /// </summary>
    public sealed class EyeStateMerger
    {
        /// <summary>Old behaviour: no face in view counts as closed eyes (looking away fast-forwards time).</summary>
        public bool TreatMissingFaceAsClosed { get; set; }

        IEyeStateSource _closedBy; // the source that saw the eyes closed last frame

        public EyeStateMerger(bool treatMissingFaceAsClosed = false) => TreatMissingFaceAsClosed = treatMissingFaceAsClosed;

        public EyeReading Read(IReadOnlyList<IEyeStateSource> sources)
        {
            string firstActive = null;
            IEyeStateSource closedBy = null;
            for (int i = 0; i < sources.Count; i++)
            {
                var source = sources[i];
                if (source == null || !source.IsActive) continue;
                firstActive ??= source.SourceName;
                if (!IsClosed(source.Current)) continue;
                closedBy = source;
                break;
            }

            bool faceLost = closedBy == null && _closedBy != null
                            && (!_closedBy.IsActive || !IsPresent(_closedBy.Current));
            _closedBy = closedBy;
            return new EyeReading(closedBy != null, faceLost, closedBy?.SourceName ?? firstActive ?? "none");
        }

        /// <summary>Forgets which source closed the eyes (e.g. after the sources change).</summary>
        public void Reset() => _closedBy = null;

        // A source that can't see a face can't see closed eyes, whatever its "closed" flag says.
        bool IsClosed(EyeState state) => state.Present ? state.Closed : TreatMissingFaceAsClosed;

        bool IsPresent(EyeState state) => state.Present || TreatMissingFaceAsClosed;
    }
}
