package com.ekkus93.weachy.camera;

import android.hardware.camera2.CameraAccessException;

import androidx.camera.core.CameraState;

final class ReachyCameraErrorUtil {

    private ReachyCameraErrorUtil() {
    }

    static String cameraAccessCode(CameraAccessException exception) {
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

    static String cameraStateErrorCode(int code) {
        switch (code) {
            case CameraState.ERROR_STREAM_CONFIG:
                return "camera_stream_config";
            case CameraState.ERROR_CAMERA_IN_USE:
                return "camera_in_use";
            case CameraState.ERROR_MAX_CAMERAS_IN_USE:
                return "max_cameras_in_use";
            case CameraState.ERROR_OTHER_RECOVERABLE_ERROR:
                return "camera_recoverable_error";
            case CameraState.ERROR_CAMERA_DISABLED:
                return "camera_disabled";
            case CameraState.ERROR_CAMERA_FATAL_ERROR:
                return "camera_fatal_error";
            case CameraState.ERROR_DO_NOT_DISTURB_MODE_ENABLED:
                return "camera_do_not_disturb_enabled";
            case CameraState.ERROR_CAMERA_REMOVED:
                return "camera_removed";
            default:
                return "camera_state_error_" + code;
        }
    }

    static boolean cameraStateErrorIsUnavailable(int code) {
        return code == CameraState.ERROR_CAMERA_IN_USE ||
                code == CameraState.ERROR_MAX_CAMERAS_IN_USE ||
                code == CameraState.ERROR_CAMERA_DISABLED ||
                code == CameraState.ERROR_DO_NOT_DISTURB_MODE_ENABLED ||
                code == CameraState.ERROR_CAMERA_REMOVED;
    }

    static String cameraStateErrorDetail(
            CameraState.StateError cameraError) {
        Throwable cause = cameraError.getCause();
        if (cause != null) {
            return safeMessage(cause);
        }
        return "CameraX reported " +
                cameraError.getType() +
                " error " +
                cameraStateErrorCode(cameraError.getCode()) + ".";
    }

    static String safeMessage(Throwable throwable) {
        String detail = throwable.getMessage();
        return detail == null || detail.trim().isEmpty()
                ? throwable.getClass().getSimpleName()
                : detail;
    }
}
