package com.ekkus93.weachy.camera;

import android.Manifest;
import android.app.Activity;
import android.content.Context;
import android.content.Intent;
import android.content.pm.PackageManager;
import android.graphics.ImageFormat;
import android.graphics.Rect;
import android.hardware.camera2.CameraAccessException;
import android.hardware.camera2.CameraCharacteristics;
import android.hardware.camera2.CameraManager;
import android.hardware.camera2.params.StreamConfigurationMap;
import android.net.Uri;
import android.os.Handler;
import android.os.Looper;
import android.provider.Settings;
import android.util.Size;

import org.json.JSONArray;
import org.json.JSONException;
import org.json.JSONObject;

import java.util.Arrays;
import java.util.Comparator;
import java.util.Map;
import java.util.concurrent.ConcurrentHashMap;

public final class ReachyCameraDiscoveryBridge {
    private static final Object CALLBACK_LOCK = new Object();
    private static final Map<String, Boolean> AVAILABILITY =
            new ConcurrentHashMap<>();
    private static final String CALIBRATION_FALLBACK =
            "Persist a versioned checkerboard calibration. Until then, use an explicitly " +
            "uncalibrated pinhole estimate derived from the active sensor array and selected " +
            "analysis resolution; never label the fallback calibrated.";

    private static CameraManager registeredManager;
    private static CameraManager.AvailabilityCallback availabilityCallback;

    private ReachyCameraDiscoveryBridge() {
    }

    public static boolean shouldShowCameraPermissionRationale(Activity activity) {
        requireActivity(activity);
        return activity.shouldShowRequestPermissionRationale(Manifest.permission.CAMERA);
    }

    public static void openApplicationSettings(Activity activity) {
        requireActivity(activity);
        Intent intent = new Intent(Settings.ACTION_APPLICATION_DETAILS_SETTINGS);
        intent.setData(Uri.fromParts("package", activity.getPackageName(), null));
        intent.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK);
        activity.startActivity(intent);
    }

    public static String discover(Activity activity) {
        try {
            requireActivity(activity);
            if (activity.checkSelfPermission(Manifest.permission.CAMERA)
                    != PackageManager.PERMISSION_GRANTED) {
                return error("permission_denied",
                        "Android camera permission is not granted.");
            }

            CameraManager manager = (CameraManager) activity.getSystemService(
                    Context.CAMERA_SERVICE);
            if (manager == null) {
                return error("camera_service_unavailable",
                        "Android returned no camera service.");
            }
            ensureAvailabilityCallback(manager);

            JSONArray cameras = new JSONArray();
            String[] cameraIds = manager.getCameraIdList();
            Arrays.sort(cameraIds);
            for (String cameraId : cameraIds) {
                cameras.put(buildCamera(manager, cameraId));
            }

            JSONObject root = new JSONObject();
            root.put("status", "ok");
            root.put("errorCode", "");
            root.put("message",
                    "Enumerated " + cameraIds.length +
                    " Android camera(s). Availability is monitored without opening a camera device.");
            root.put("cameras", cameras);
            return root.toString();
        } catch (SecurityException exception) {
            return error("permission_denied", safeMessage(exception));
        } catch (CameraAccessException exception) {
            return error(cameraAccessCode(exception), safeMessage(exception));
        } catch (JSONException exception) {
            return error("json_encoding_failed", safeMessage(exception));
        } catch (RuntimeException exception) {
            return error("camera_discovery_runtime_error", safeMessage(exception));
        }
    }

    public static void shutdown(Activity activity) {
        requireActivity(activity);
        synchronized (CALLBACK_LOCK) {
            if (registeredManager != null && availabilityCallback != null) {
                registeredManager.unregisterAvailabilityCallback(availabilityCallback);
            }
            registeredManager = null;
            availabilityCallback = null;
            AVAILABILITY.clear();
        }
    }

    private static JSONObject buildCamera(
            CameraManager manager,
            String cameraId) throws CameraAccessException, JSONException {
        CameraCharacteristics characteristics =
                manager.getCameraCharacteristics(cameraId);
        JSONObject camera = new JSONObject();
        camera.put("id", cameraId);
        camera.put("facing", facingLabel(
                characteristics.get(CameraCharacteristics.LENS_FACING)));
        Integer orientation = characteristics.get(
                CameraCharacteristics.SENSOR_ORIENTATION);
        camera.put("sensorOrientationDegrees", orientation == null ? 0 : orientation);
        camera.put("hardwareLevel", hardwareLevelLabel(
                characteristics.get(
                        CameraCharacteristics.INFO_SUPPORTED_HARDWARE_LEVEL)));
        camera.put("availability", availabilityLabel(cameraId));
        camera.put("analysisResolutions", analysisResolutions(characteristics));

        Rect activeArray = characteristics.get(
                CameraCharacteristics.SENSOR_INFO_ACTIVE_ARRAY_SIZE);
        camera.put("activeArrayWidth", activeArray == null ? 0 : activeArray.width());
        camera.put("activeArrayHeight", activeArray == null ? 0 : activeArray.height());
        camera.put("intrinsics", intrinsics(characteristics));
        camera.put("calibrationFallback", CALIBRATION_FALLBACK);
        return camera;
    }

    private static JSONArray analysisResolutions(
            CameraCharacteristics characteristics) throws JSONException {
        StreamConfigurationMap map = characteristics.get(
                CameraCharacteristics.SCALER_STREAM_CONFIGURATION_MAP);
        Size[] sizes = map == null
                ? null
                : map.getOutputSizes(ImageFormat.YUV_420_888);
        if (sizes == null) {
            return new JSONArray();
        }
        Arrays.sort(sizes, new Comparator<Size>() {
            @Override
            public int compare(Size left, Size right) {
                long leftPixels = (long) left.getWidth() * left.getHeight();
                long rightPixels = (long) right.getWidth() * right.getHeight();
                int byPixels = Long.compare(rightPixels, leftPixels);
                if (byPixels != 0) {
                    return byPixels;
                }
                int byWidth = Integer.compare(right.getWidth(), left.getWidth());
                if (byWidth != 0) {
                    return byWidth;
                }
                return Integer.compare(right.getHeight(), left.getHeight());
            }
        });

        JSONArray resolutions = new JSONArray();
        Size previous = null;
        for (Size size : sizes) {
            if (previous != null
                    && previous.getWidth() == size.getWidth()
                    && previous.getHeight() == size.getHeight()) {
                continue;
            }
            JSONObject resolution = new JSONObject();
            resolution.put("width", size.getWidth());
            resolution.put("height", size.getHeight());
            resolutions.put(resolution);
            previous = size;
        }
        return resolutions;
    }

    private static JSONObject intrinsics(
            CameraCharacteristics characteristics) throws JSONException {
        float[] calibration = characteristics.get(
                CameraCharacteristics.LENS_INTRINSIC_CALIBRATION);
        JSONObject intrinsics = new JSONObject();
        boolean available = calibration != null
                && calibration.length >= 5
                && calibration[0] > 0.0f
                && calibration[1] > 0.0f;
        intrinsics.put("available", available);
        intrinsics.put("fx", available ? calibration[0] : 0.0f);
        intrinsics.put("fy", available ? calibration[1] : 0.0f);
        intrinsics.put("cx", available ? calibration[2] : 0.0f);
        intrinsics.put("cy", available ? calibration[3] : 0.0f);
        intrinsics.put("skew", available ? calibration[4] : 0.0f);
        return intrinsics;
    }

    private static void ensureAvailabilityCallback(CameraManager manager) {
        synchronized (CALLBACK_LOCK) {
            if (registeredManager == manager && availabilityCallback != null) {
                return;
            }
            if (registeredManager != null && availabilityCallback != null) {
                registeredManager.unregisterAvailabilityCallback(availabilityCallback);
            }
            AVAILABILITY.clear();
            availabilityCallback = new CameraManager.AvailabilityCallback() {
                @Override
                public void onCameraAvailable(String cameraId) {
                    AVAILABILITY.put(cameraId, Boolean.TRUE);
                }

                @Override
                public void onCameraUnavailable(String cameraId) {
                    AVAILABILITY.put(cameraId, Boolean.FALSE);
                }
            };
            registeredManager = manager;
            manager.registerAvailabilityCallback(
                    availabilityCallback,
                    new Handler(Looper.getMainLooper()));
        }
    }

    private static String availabilityLabel(String cameraId) {
        Boolean available = AVAILABILITY.get(cameraId);
        if (available == null) {
            return "unknown";
        }
        return available ? "available" : "in_use_or_unavailable";
    }

    private static String facingLabel(Integer facing) {
        if (facing == null) {
            return "unknown";
        }
        if (facing == CameraCharacteristics.LENS_FACING_FRONT) {
            return "front";
        }
        if (facing == CameraCharacteristics.LENS_FACING_BACK) {
            return "rear";
        }
        if (facing == CameraCharacteristics.LENS_FACING_EXTERNAL) {
            return "external";
        }
        return "unknown";
    }

    private static String hardwareLevelLabel(Integer level) {
        if (level == null) {
            return "unknown";
        }
        if (level == CameraCharacteristics.INFO_SUPPORTED_HARDWARE_LEVEL_LEGACY) {
            return "legacy";
        }
        if (level == CameraCharacteristics.INFO_SUPPORTED_HARDWARE_LEVEL_LIMITED) {
            return "limited";
        }
        if (level == CameraCharacteristics.INFO_SUPPORTED_HARDWARE_LEVEL_FULL) {
            return "full";
        }
        if (level == CameraCharacteristics.INFO_SUPPORTED_HARDWARE_LEVEL_3) {
            return "level_3";
        }
        if (level == CameraCharacteristics.INFO_SUPPORTED_HARDWARE_LEVEL_EXTERNAL) {
            return "external";
        }
        return "unknown";
    }

    private static String cameraAccessCode(CameraAccessException exception) {
        switch (exception.getReason()) {
            case CameraAccessException.CAMERA_DISABLED:
                return "camera_disabled";
            case CameraAccessException.CAMERA_DISCONNECTED:
                return "camera_disconnected";
            case CameraAccessException.CAMERA_IN_USE:
                return "camera_in_use";
            case CameraAccessException.MAX_CAMERAS_IN_USE:
                return "max_cameras_in_use";
            case CameraAccessException.CAMERA_ERROR:
            default:
                return "camera_access_error";
        }
    }

    private static String error(String code, String message) {
        try {
            JSONObject root = new JSONObject();
            root.put("status", "error");
            root.put("errorCode", code);
            root.put("message", message);
            root.put("cameras", new JSONArray());
            return root.toString();
        } catch (JSONException exception) {
            return "{\"status\":\"error\",\"errorCode\":\"json_encoding_failed\",\"message\":\"Camera discovery failed while encoding diagnostics.\",\"cameras\":[]}";
        }
    }

    private static String safeMessage(Throwable throwable) {
        String message = throwable.getMessage();
        return message == null || message.trim().isEmpty()
                ? throwable.getClass().getSimpleName()
                : message;
    }

    private static void requireActivity(Activity activity) {
        if (activity == null) {
            throw new IllegalArgumentException("The Unity activity is required.");
        }
    }
}
