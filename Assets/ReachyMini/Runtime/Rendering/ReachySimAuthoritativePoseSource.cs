#nullable enable

using System;
using System.Collections.Generic;
using ReachyMini.Interop;
using ReachyMini.Presentation;

namespace ReachyMini.Rendering
{
    public sealed class ReachySimAuthoritativePoseSource :
        IReachyAuthoritativePoseSource,
        IDisposable
    {
        private readonly IReachySimAuthoritativeStateReader stateReader;
        private readonly bool ownsStateReader;
        private readonly string[] bodyNames;
        private readonly ReachySimAuthoritativeStateFrame stateFrame;
        private readonly ReachyAuthoritativePoseBuffer poseBuffer =
            new ReachyAuthoritativePoseBuffer();
        private bool hasPublishedState;
        private ulong lastSequence;
        private double lastSimulationTime;
        private uint lastContinuityId;
        private bool disposed;

        public ReachySimAuthoritativePoseSource(
            ReachySimSession session,
            IReadOnlyList<string> canonicalBodyNames)
            : this(
                new ReachySimAuthoritativeStateReader(
                    session ?? throw new ArgumentNullException(nameof(session))),
                canonicalBodyNames,
                ownsStateReader: true)
        {
        }

        public ReachySimAuthoritativePoseSource(
            IReachySimAuthoritativeStateReader stateReader,
            IReadOnlyList<string> canonicalBodyNames,
            bool ownsStateReader = false)
        {
            this.stateReader = stateReader ??
                throw new ArgumentNullException(nameof(stateReader));
            this.ownsStateReader = ownsStateReader;
            if (canonicalBodyNames == null)
            {
                throw new ArgumentNullException(nameof(canonicalBodyNames));
            }
            if (canonicalBodyNames.Count != stateReader.Layout.BodyPoseCount)
            {
                throw new ArgumentException(
                    $"The canonical body-name count {canonicalBodyNames.Count} " +
                    $"does not match the native body-pose count " +
                    $"{stateReader.Layout.BodyPoseCount}.",
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
            stateFrame = stateReader.CreateFrame();
        }

        public ulong ModelHash => stateReader.Layout.ModelHash;

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

        public bool TryGetLatestPair(
            out ReachyAuthoritativePoseSnapshot older,
            out ReachyAuthoritativePoseSnapshot newer)
        {
            ThrowIfDisposed();
            stateReader.Capture(stateFrame);
            if (!hasPublishedState ||
                stateFrame.Sequence != lastSequence ||
                stateFrame.SimulationTime != lastSimulationTime ||
                stateFrame.ContinuityId != lastContinuityId)
            {
                PublishCurrentState();
            }

            return poseBuffer.TryGetLatestPair(out older, out newer);
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }
            disposed = true;
            if (ownsStateReader)
            {
                stateReader.Dispose();
            }
            GC.SuppressFinalize(this);
        }

        private void PublishCurrentState()
        {
            ReachyMujocoBodyPose[] poses =
                new ReachyMujocoBodyPose[stateFrame.BodyPoseCount];
            for (int index = 0; index < poses.Length; ++index)
            {
                ReachySimBodyPoseSnapshot nativePose =
                    stateFrame.GetBodyPose(index);
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

            poseBuffer.Publish(
                new ReachyAuthoritativePoseSnapshot(
                    stateFrame.Sequence,
                    stateFrame.SimulationTime,
                    stateFrame.ContinuityId,
                    poses));
            hasPublishedState = true;
            lastSequence = stateFrame.Sequence;
            lastSimulationTime = stateFrame.SimulationTime;
            lastContinuityId = stateFrame.ContinuityId;
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
