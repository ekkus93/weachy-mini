#nullable enable

using System;
using System.Globalization;
using System.IO;
using ReachyMini.Security;
using UnityEngine;

namespace ReachyMini.AppState
{
    public sealed class ReachyCameraCalibrationPersistenceChangedEventArgs : EventArgs
    {
        public ReachyCameraCalibrationPersistenceChangedEventArgs(
            bool isDegraded,
            string message,
            ulong revision)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                throw new ArgumentException(
                    "Calibration persistence state requires diagnostics.",
                    nameof(message));
            }

            IsDegraded = isDegraded;
            Message = message;
            Revision = revision;
        }

        public bool IsDegraded { get; }

        public string Message { get; }

        public ulong Revision { get; }
    }

    public sealed class ReachyCameraCalibrationPersistenceStore : IDisposable
    {
        public const string CalibrationFileName =
            "reachy-camera-calibration-v1.json";
        public const int SchemaVersion = 1;
        public const int MaximumProfiles = ReachyCameraCalibrationStateStore.MaximumProfiles;

        private readonly string persistencePath;
        private bool initialized;
        private bool disposed;
        private bool loading;
        private string lastSerialized = string.Empty;
        private ulong persistenceRevision;

        public ReachyCameraCalibrationPersistenceStore(string calibrationPath)
        {
            if (string.IsNullOrWhiteSpace(calibrationPath))
            {
                throw new ArgumentException(
                    "Camera calibration persistence requires a file path.",
                    nameof(calibrationPath));
            }

            persistencePath = Path.GetFullPath(calibrationPath);
        }

        public ReachyCameraCalibrationStateStore State { get; } =
            new ReachyCameraCalibrationStateStore();

        public string PersistencePath => persistencePath;

        public string LastPersistenceFault { get; private set; } = string.Empty;

        public bool IsDegraded => !string.IsNullOrEmpty(LastPersistenceFault);

        public event EventHandler<
            ReachyCameraCalibrationPersistenceChangedEventArgs>? Changed;

        public void Initialize()
        {
            ThrowIfDisposed();
            if (initialized)
            {
                throw new InvalidOperationException(
                    "Camera calibration persistence cannot initialize more than once.");
            }

            initialized = true;
            State.Changed += OnCalibrationsChanged;
            if (!File.Exists(persistencePath))
            {
                PersistCurrent();
                Publish(
                    isDegraded: false,
                    $"Camera calibration storage initialized at {persistencePath}.");
                return;
            }

            loading = true;
            try
            {
                string json = ReachyImportedContentPolicy.ReadBoundedUtf8File(
                    persistencePath,
                    ReachyImportedDocumentKind.CameraCalibration);
                CalibrationEnvelope envelope =
                    JsonUtility.FromJson<CalibrationEnvelope>(json) ??
                    throw new InvalidDataException(
                        "The camera calibration file did not contain a JSON object.");
                if (envelope.schemaVersion != SchemaVersion)
                {
                    throw new InvalidDataException(
                        $"Unsupported camera calibration schema {envelope.schemaVersion}; " +
                        $"expected {SchemaVersion}.");
                }

                CalibrationProfileDto[] profileDtos =
                    envelope.profiles ?? Array.Empty<CalibrationProfileDto>();
                if (profileDtos.Length > MaximumProfiles)
                {
                    throw new InvalidDataException(
                        $"Camera calibration storage supports at most {MaximumProfiles} profiles.");
                }
                var profiles = new ReachyCameraCalibrationProfile[profileDtos.Length];
                for (int index = 0; index < profileDtos.Length; ++index)
                {
                    CalibrationProfileDto profileDto = profileDtos[index] ??
                        throw new InvalidDataException(
                            "The camera calibration file contains a null profile.");
                    profiles[index] = profileDto.ToProfile();
                }

                State.ReplaceAll(profiles);
                lastSerialized = Serialize(State.Current);
                LastPersistenceFault = string.Empty;
                Publish(
                    isDegraded: false,
                    $"Restored {profiles.Length} camera calibration profile(s) " +
                    $"from {persistencePath}.");
            }
            catch (Exception exception)
            {
                string quarantinePath = QuarantineInvalidFile();
                State.ReplaceAll(Array.Empty<ReachyCameraCalibrationProfile>());
                PersistCurrent();
                LastPersistenceFault = exception.Message;
                Publish(
                    isDegraded: true,
                    "Invalid camera calibration data was quarantined and no " +
                    $"calibration was silently substituted. source={persistencePath}; " +
                    $"quarantine={quarantinePath}; error={exception.Message}");
            }
            finally
            {
                loading = false;
            }
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            State.Changed -= OnCalibrationsChanged;
        }

        private void OnCalibrationsChanged(
            object? sender,
            ReachyCameraCalibrationChangedEventArgs eventArgs)
        {
            if (loading || disposed)
            {
                return;
            }

            try
            {
                PersistCurrent();
                bool recovered = !string.IsNullOrEmpty(LastPersistenceFault);
                LastPersistenceFault = string.Empty;
                Publish(
                    isDegraded: false,
                    recovered
                        ? $"Camera calibration persistence recovered at {persistencePath}."
                        : $"Persisted {eventArgs.Snapshot.Profiles.Count} camera " +
                            $"calibration profile(s) to {persistencePath}.");
            }
            catch (Exception exception)
            {
                LastPersistenceFault = exception.Message;
                Publish(
                    isDegraded: true,
                    $"Camera calibration could not be saved to {persistencePath}: " +
                    exception.Message);
            }
        }

        private void PersistCurrent()
        {
            string json = Serialize(State.Current);
            ReachyImportedContentPolicy.RequireBoundedUtf8Text(
                json,
                ReachyImportedDocumentKind.CameraCalibration);
            if (string.Equals(json, lastSerialized, StringComparison.Ordinal) &&
                File.Exists(persistencePath))
            {
                return;
            }

            string? directory = Path.GetDirectoryName(persistencePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            string temporaryPath = persistencePath + ".tmp";
            string backupPath = persistencePath + ".bak";
            File.WriteAllText(temporaryPath, json);
            try
            {
                if (File.Exists(persistencePath))
                {
                    File.Copy(persistencePath, backupPath, overwrite: true);
                    File.Delete(persistencePath);
                }
                File.Move(temporaryPath, persistencePath);
                if (File.Exists(backupPath))
                {
                    File.Delete(backupPath);
                }
                lastSerialized = json;
            }
            catch
            {
                if (!File.Exists(persistencePath) && File.Exists(backupPath))
                {
                    File.Copy(backupPath, persistencePath, overwrite: true);
                }
                throw;
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
        }

        private string QuarantineInvalidFile()
        {
            string stamp = DateTime.UtcNow.ToString(
                "yyyyMMddHHmmssfff",
                CultureInfo.InvariantCulture);
            string quarantinePath =
                persistencePath + $".corrupt-{stamp}";
            File.Move(persistencePath, quarantinePath);
            return quarantinePath;
        }

        private void Publish(bool isDegraded, string message)
        {
            ulong revision = checked(++persistenceRevision);
            Changed?.Invoke(
                this,
                new ReachyCameraCalibrationPersistenceChangedEventArgs(
                    isDegraded,
                    message,
                    revision));
        }

        private void ThrowIfDisposed()
        {
            if (disposed)
            {
                throw new ObjectDisposedException(
                    nameof(ReachyCameraCalibrationPersistenceStore));
            }
        }

        private static string Serialize(
            ReachyCameraCalibrationSnapshot snapshot)
        {
            return JsonUtility.ToJson(
                CalibrationEnvelope.FromSnapshot(snapshot),
                prettyPrint: true);
        }

        [Serializable]
        private sealed class CalibrationEnvelope
        {
            public int schemaVersion = SchemaVersion;
            public CalibrationProfileDto[]? profiles;

            public static CalibrationEnvelope FromSnapshot(
                ReachyCameraCalibrationSnapshot snapshot)
            {
                if (snapshot == null)
                {
                    throw new ArgumentNullException(nameof(snapshot));
                }

                var profileDtos = new CalibrationProfileDto[
                    snapshot.Profiles.Count];
                for (int index = 0; index < snapshot.Profiles.Count; ++index)
                {
                    profileDtos[index] = CalibrationProfileDto.FromProfile(
                        snapshot.Profiles[index]);
                }
                return new CalibrationEnvelope
                {
                    schemaVersion = SchemaVersion,
                    profiles = profileDtos,
                };
            }
        }

        [Serializable]
        private sealed class CalibrationProfileDto
        {
            public int profileSchemaVersion;
            public string profileId = string.Empty;
            public string cameraId = string.Empty;
            public int facing;
            public int provenance;
            public string provenanceDetail = string.Empty;
            public string sourceReference = string.Empty;
            public string modelCompatibility = string.Empty;
            public string createdUtc = string.Empty;
            public int reprojectionMode;
            public bool translationIgnored;
            public ImageNormalizationDto? imageNormalization;
            public IntrinsicMatrixDto? phoneIntrinsics;
            public IntrinsicMatrixDto? reachyIntrinsics;
            public QuaternionDto? neutralReachyFromPhoneRotation;

            public ReachyCameraCalibrationProfile ToProfile()
            {
                if (reprojectionMode !=
                    (int)ReachyCameraReprojectionMode.RotationOnly)
                {
                    throw new InvalidDataException(
                        $"Unsupported reprojection mode {reprojectionMode}.");
                }
                if (!translationIgnored)
                {
                    throw new InvalidDataException(
                        "RMA-100 calibration must explicitly mark translation as ignored.");
                }
                if (imageNormalization == null ||
                    phoneIntrinsics == null ||
                    reachyIntrinsics == null ||
                    neutralReachyFromPhoneRotation == null)
                {
                    throw new InvalidDataException(
                        "Camera calibration omitted required matrix or normalization data.");
                }

                DateTimeOffset created = DateTimeOffset.Parse(
                    createdUtc,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind).ToUniversalTime();
                return new ReachyCameraCalibrationProfile(
                    profileSchemaVersion,
                    profileId,
                    cameraId,
                    (ReachyDeviceCameraFacing)facing,
                    (ReachyCameraCalibrationProvenance)provenance,
                    provenanceDetail,
                    sourceReference,
                    modelCompatibility,
                    created,
                    imageNormalization.ToNormalization(),
                    phoneIntrinsics.ToIntrinsics(),
                    reachyIntrinsics.ToIntrinsics(),
                    neutralReachyFromPhoneRotation.ToQuaternion());
            }

            public static CalibrationProfileDto FromProfile(
                ReachyCameraCalibrationProfile profile)
            {
                if (profile == null)
                {
                    throw new ArgumentNullException(nameof(profile));
                }

                return new CalibrationProfileDto
                {
                    profileSchemaVersion = profile.ProfileSchemaVersion,
                    profileId = profile.ProfileId,
                    cameraId = profile.CameraId,
                    facing = (int)profile.Facing,
                    provenance = (int)profile.Provenance,
                    provenanceDetail = profile.ProvenanceDetail,
                    sourceReference = profile.SourceReference,
                    modelCompatibility = profile.ModelCompatibility,
                    createdUtc = profile.CreatedUtc.ToString(
                        "O",
                        CultureInfo.InvariantCulture),
                    reprojectionMode = (int)profile.ReprojectionMode,
                    translationIgnored = true,
                    imageNormalization =
                        ImageNormalizationDto.FromNormalization(
                            profile.ImageNormalization),
                    phoneIntrinsics = IntrinsicMatrixDto.FromIntrinsics(
                        profile.PhoneIntrinsics),
                    reachyIntrinsics = IntrinsicMatrixDto.FromIntrinsics(
                        profile.ReachyIntrinsics),
                    neutralReachyFromPhoneRotation =
                        QuaternionDto.FromQuaternion(
                            profile.NeutralReachyFromPhoneRotation),
                };
            }
        }

        [Serializable]
        private sealed class ImageNormalizationDto
        {
            public int sourceWidth;
            public int sourceHeight;
            public int cropLeft;
            public int cropTop;
            public int cropWidth;
            public int cropHeight;
            public int clockwiseRotationDegrees;
            public bool mirrorHorizontally;

            public ReachyCameraImageNormalization ToNormalization()
            {
                return new ReachyCameraImageNormalization(
                    sourceWidth,
                    sourceHeight,
                    cropLeft,
                    cropTop,
                    cropWidth,
                    cropHeight,
                    clockwiseRotationDegrees,
                    mirrorHorizontally);
            }

            public static ImageNormalizationDto FromNormalization(
                ReachyCameraImageNormalization normalization)
            {
                if (normalization == null)
                {
                    throw new ArgumentNullException(nameof(normalization));
                }

                return new ImageNormalizationDto
                {
                    sourceWidth = normalization.SourceWidth,
                    sourceHeight = normalization.SourceHeight,
                    cropLeft = normalization.CropLeft,
                    cropTop = normalization.CropTop,
                    cropWidth = normalization.CropWidth,
                    cropHeight = normalization.CropHeight,
                    clockwiseRotationDegrees =
                        normalization.ClockwiseRotationDegrees,
                    mirrorHorizontally = normalization.MirrorHorizontally,
                };
            }
        }

        [Serializable]
        private sealed class IntrinsicMatrixDto
        {
            public int imageWidth;
            public int imageHeight;
            public Matrix3x3Dto? pixelFromOpticalRay;

            public ReachyCameraIntrinsicMatrix ToIntrinsics()
            {
                return new ReachyCameraIntrinsicMatrix(
                    imageWidth,
                    imageHeight,
                    (pixelFromOpticalRay ?? throw new InvalidDataException(
                        "Camera intrinsics omitted the projection matrix."))
                        .ToMatrix());
            }

            public static IntrinsicMatrixDto FromIntrinsics(
                ReachyCameraIntrinsicMatrix intrinsics)
            {
                if (intrinsics == null)
                {
                    throw new ArgumentNullException(nameof(intrinsics));
                }

                return new IntrinsicMatrixDto
                {
                    imageWidth = intrinsics.ImageWidth,
                    imageHeight = intrinsics.ImageHeight,
                    pixelFromOpticalRay = Matrix3x3Dto.FromMatrix(
                        intrinsics.PixelFromOpticalRay),
                };
            }
        }

        [Serializable]
        private sealed class Matrix3x3Dto
        {
            public double m00;
            public double m01;
            public double m02;
            public double m10;
            public double m11;
            public double m12;
            public double m20;
            public double m21;
            public double m22;

            public ReachyMatrix3x3 ToMatrix()
            {
                return new ReachyMatrix3x3(
                    m00,
                    m01,
                    m02,
                    m10,
                    m11,
                    m12,
                    m20,
                    m21,
                    m22);
            }

            public static Matrix3x3Dto FromMatrix(ReachyMatrix3x3 matrix)
            {
                return new Matrix3x3Dto
                {
                    m00 = matrix.M00,
                    m01 = matrix.M01,
                    m02 = matrix.M02,
                    m10 = matrix.M10,
                    m11 = matrix.M11,
                    m12 = matrix.M12,
                    m20 = matrix.M20,
                    m21 = matrix.M21,
                    m22 = matrix.M22,
                };
            }
        }

        [Serializable]
        private sealed class QuaternionDto
        {
            public double x;
            public double y;
            public double z;
            public double w;

            public ReachyQuaternionD ToQuaternion()
            {
                return new ReachyQuaternionD(x, y, z, w);
            }

            public static QuaternionDto FromQuaternion(
                ReachyQuaternionD quaternion)
            {
                return new QuaternionDto
                {
                    x = quaternion.X,
                    y = quaternion.Y,
                    z = quaternion.Z,
                    w = quaternion.W,
                };
            }
        }
    }
}
