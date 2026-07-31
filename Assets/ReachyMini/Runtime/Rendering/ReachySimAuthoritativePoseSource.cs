#nullable enable

using System;
using System.Collections.Generic;
using ReachyMini.Interop;
using ReachyMini.Presentation;
using ReachyMini.Simulation;

namespace ReachyMini.Rendering
{
    public sealed class ReachySimAuthoritativePoseSource :
        IReachyReusableAuthoritativePoseSource,
        IDisposable
    {
        private readonly object stateGate = new object();
        private readonly IReachySimAuthoritativeStateReader? stateReader;
        private readonly IReachyPublishedAuthoritativeStateSource? publishedStateSource;
        private readonly bool ownsStateReader;
        private readonly ReachySimAuthoritativeStateLayout layout;
        private readonly string[] bodyNames;
        private ReachySimAuthoritativeStateFrame previousStateFrame;
        private ReachySimAuthoritativeStateFrame latestStateFrame;
        private ReachySimAuthoritativeStateFrame captureStateFrame;
        private bool hasLatestState;
        private bool hasPreviousState;
        private bool disposed;

        public ReachySimAuthoritativePoseSource(
            ReachySimSession session,
            IReadOnlyList<string> canonicalBodyNames)
            : this(
                new ReachySimAuthoritativeStateReader(
                    session ?? throw new ArgumentNullException(nameof(session))),
                publishedStateSource: null,
                canonicalBodyNames,
                ownsStateReader: true)
        {
        }

        public ReachySimAuthoritativePoseSource(
            IReachySimAuthoritativeStateReader stateReader,
            IReadOnlyList<string> canonicalBodyNames,
            bool ownsStateReader = false)
            : this(
                stateReader ?? throw new ArgumentNullException(nameof(stateReader)),
                publishedStateSource: null,
                canonicalBodyNames,
                ownsStateReader)
        {
        }

        public ReachySimAuthoritativePoseSource(
            IReachyPublishedAuthoritativeStateSource publishedStateSource,
            IReadOnlyList<string> canonicalBodyNames)
            : this(
                stateReader: null,
                publishedStateSource ??
                    throw new ArgumentNullException(nameof(publishedStateSource)),
                canonicalBodyNames,
                ownsStateReader: false)
        {
        }

        private ReachySimAuthoritativePoseSource(
            IReachySimAuthoritativeStateReader? stateReader,
            IReachyPublishedAuthoritativeStateSource? publishedStateSource,
            IReadOnlyList<string> canonicalBodyNames,
            bool ownsStateReader)
        {
            if ((stateReader == null) == (publishedStateSource == null))
            {
                throw new ArgumentException(
                    "Exactly one authoritative state source must be provided.");
            }
            this.stateReader = stateReader;
            this.publishedStateSource = publishedStateSource;
            this.ownsStateReader = ownsStateReader;
            layout = stateReader?.Layout ??
                publishedStateSource!.AuthoritativeStateLayout;
            if (canonicalBodyNames == null)
            {
                throw new ArgumentNullException(nameof(canonicalBodyNames));
            }
            if (canonicalBodyNames.Count != layout.BodyPoseCount)
            {
                throw new ArgumentException(
                    $"The canonical body-name count {canonicalBodyNames.Count} " +
                    $"does not match the authoritative body-pose count " +
                    $"{layout.BodyPoseCount}.",
                    nameof(canonicalBodyNames));
            }

            bodyNames = new string[canonicalBodyNames.Count];
            for (int index = 0; index < bodyNames.Length; ++index)
            {
                string name = canonicalBodyNames[index];
                if (string.IsNullOrWhiteSpace(name))
                {
                    throw new ArgumentException(
                        $"Canonical body name {index} is missing.",
                        nameof(canonicalBodyNames));
                }
                bodyNames[index] = name;
            }

            previousStateFrame = CreateStateFrame();
            latestStateFrame = CreateStateFrame();
            captureStateFrame = CreateStateFrame();
        }

        public ulong ModelHash => layout.ModelHash;

        public int BodyCount => bodyNames.Length;

        public static ReachySimAuthoritativePoseSource Bind(
            ReachyAuthoritativeRenderer renderer,
            ReachySimSession session,
            ReachyPresentationBody[] canonicalBodies)
        {
            if (renderer == null)
            {
                throw new ArgumentNullException(nameof(renderer));
            }
            if (canonicalBodies == null)
            {
                throw new ArgumentNullException(nameof(canonicalBodies));
            }

            string[] names = new string[canonicalBodies.Length];
            for (int index = 0; index < canonicalBodies.Length; ++index)
            {
                ReachyPresentationBody body = canonicalBodies[index] ??
                    throw new ArgumentException(
                        $"Canonical body binding {index} is null.",
                        nameof(canonicalBodies));
                if (body.BodyIndex != index)
                {
                    throw new ArgumentException(
                        $"Canonical body binding {index} declares index " +
                        $"{body.BodyIndex}.",
                        nameof(canonicalBodies));
                }
                names[index] = body.BodyName;
            }

            ReachySimAuthoritativePoseSource source =
                new ReachySimAuthoritativePoseSource(session, names);
            try
            {
                renderer.BindPoseSource(source);
                return source;
            }
            catch
            {
                source.Dispose();
                throw;
            }
        }

        public ReachyReusableAuthoritativePoseFrame CreatePoseFrame()
        {
            ThrowIfDisposed();
            return new ReachyReusableAuthoritativePoseFrame(bodyNames);
        }

        public bool TryCopyLatestPair(
            ReachyReusableAuthoritativePoseFrame olderDestination,
            ReachyReusableAuthoritativePoseFrame newerDestination)
        {
            if (olderDestination == null)
            {
                throw new ArgumentNullException(nameof(olderDestination));
            }
            if (newerDestination == null)
            {
                throw new ArgumentNullException(nameof(newerDestination));
            }
            if (olderDestination.BodyCount != BodyCount ||
                newerDestination.BodyCount != BodyCount)
            {
                throw new ArgumentException(
                    "Reusable pose destinations were created for a different body mapping.");
            }

            lock (stateGate)
            {
                ThrowIfDisposed();
                CaptureLatestState();
                if (!hasPreviousState)
                {
                    return false;
                }
                olderDestination.CopyFrom(previousStateFrame);
                newerDestination.CopyFrom(latestStateFrame);
                return true;
            }
        }

        public bool TryGetLatestPair(
            out ReachyAuthoritativePoseSnapshot older,
            out ReachyAuthoritativePoseSnapshot newer)
        {
            lock (stateGate)
            {
                ThrowIfDisposed();
                CaptureLatestState();
                if (!hasPreviousState)
                {
                    older = null!;
                    newer = null!;
                    return false;
                }

                older = CreateImmutableSnapshot(previousStateFrame);
                newer = CreateImmutableSnapshot(latestStateFrame);
                return true;
            }
        }

        public void Dispose()
        {
            lock (stateGate)
            {
                if (disposed)
                {
                    return;
                }
                disposed = true;
                if (ownsStateReader)
                {
                    stateReader?.Dispose();
                }
            }
            GC.SuppressFinalize(this);
        }

        private ReachySimAuthoritativeStateFrame CreateStateFrame()
        {
            return stateReader?.CreateFrame() ??
                publishedStateSource!.CreateAuthoritativeStateFrame();
        }

        private void CaptureLatestState()
        {
            bool captured;
            if (publishedStateSource != null)
            {
                captured = publishedStateSource.TryCaptureLatestAuthoritativeState(
                    captureStateFrame);
            }
            else
            {
                stateReader!.Capture(captureStateFrame);
                captured = true;
            }
            if (!captured)
            {
                return;
            }
            if (hasLatestState && SameIdentity(captureStateFrame, latestStateFrame))
            {
                return;
            }
            if (hasLatestState &&
                captureStateFrame.ContinuityId == latestStateFrame.ContinuityId &&
                (captureStateFrame.Sequence <= latestStateFrame.Sequence ||
                 captureStateFrame.SimulationTime <= latestStateFrame.SimulationTime))
            {
                throw new InvalidOperationException(
                    "Authoritative state sequence and simulation time must increase " +
                    "within a continuity epoch.");
            }

            if (!hasLatestState)
            {
                ReachySimAuthoritativeStateFrame reusable = latestStateFrame;
                latestStateFrame = captureStateFrame;
                captureStateFrame = reusable;
                hasLatestState = true;
                return;
            }

            ReachySimAuthoritativeStateFrame oldest = previousStateFrame;
            previousStateFrame = latestStateFrame;
            latestStateFrame = captureStateFrame;
            captureStateFrame = oldest;
            hasPreviousState = true;
        }

        private ReachyAuthoritativePoseSnapshot CreateImmutableSnapshot(
            ReachySimAuthoritativeStateFrame source)
        {
            ReachyMujocoBodyPose[] poses =
                new ReachyMujocoBodyPose[source.BodyPoseCount];
            for (int index = 0; index < poses.Length; ++index)
            {
                ReachySimBodyPoseSnapshot nativePose = source.GetBodyPose(index);
                uint expectedBodyId = checked((uint)index + 1U);
                if (nativePose.BodyId != expectedBodyId)
                {
                    throw new InvalidOperationException(
                        $"Native body pose {index} declares MuJoCo body " +
                        $"{nativePose.BodyId}, expected {expectedBodyId}.");
                }
                poses[index] = new ReachyMujocoBodyPose(
                    index,
                    bodyNames[index],
                    nativePose.PositionX,
                    nativePose.PositionY,
                    nativePose.PositionZ,
                    nativePose.QuaternionW,
                    nativePose.QuaternionX,
                    nativePose.QuaternionY,
                    nativePose.QuaternionZ);
            }

            return new ReachyAuthoritativePoseSnapshot(
                source.Sequence,
                source.SimulationTime,
                source.ContinuityId,
                poses);
        }

        private static bool SameIdentity(
            ReachySimAuthoritativeStateFrame left,
            ReachySimAuthoritativeStateFrame right)
        {
            return left.Sequence == right.Sequence &&
                left.SimulationTime == right.SimulationTime &&
                left.ContinuityId == right.ContinuityId;
        }

        private void ThrowIfDisposed()
        {
            if (disposed)
            {
                throw new ObjectDisposedException(
                    nameof(ReachySimAuthoritativePoseSource));
            }
        }
    }
}
