package com.ekkus93.weachy.camera;

import android.app.Activity;
import android.content.Context;
import android.graphics.Rect;
import android.hardware.camera2.CameraAccessException;
import android.hardware.camera2.CameraCharacteristics;
import android.hardware.camera2.CameraManager;

final class ReachyCameraDescriptor {
    final String cameraId;
    final String facing;
    final int sensorOrientationDegrees;
    final Rect activeArray;
    final ReachyFrameIntrinsics intrinsics;

    ReachyCameraDescriptor(
            String cameraId,
            String facing,
            int sensorOrientationDegrees,
            Rect activeArray,
            ReachyFrameIntrinsics intrinsics) {
        this.cameraId = cameraId;
        this.facing = facing;
        this.sensorOrientationDegrees = sensorOrientationDegrees;
        this.activeArray = new Rect(activeArray);
        this.intrinsics = intrinsics;
    }

    static ReachyCameraDescriptor load(
            Activity activity,
            String selectedCameraId) throws CameraAccessException {
        CameraManager manager = (CameraManager) activity.getSystemService(
                Context.CAMERA_SERVICE);
        if (manager == null) {
            throw new IllegalStateException(
                    "Android returned no camera service.");
        }
        CameraCharacteristics characteristics =
                manager.getCameraCharacteristics(selectedCameraId);
        Integer facingValue = characteristics.get(
                CameraCharacteristics.LENS_FACING);
        Integer orientationValue = characteristics.get(
                CameraCharacteristics.SENSOR_ORIENTATION);
        Rect activeArrayValue = characteristics.get(
                CameraCharacteristics.SENSOR_INFO_ACTIVE_ARRAY_SIZE);
        if (activeArrayValue == null ||
                activeArrayValue.width() <= 0 ||
                activeArrayValue.height() <= 0) {
            throw new IllegalStateException(
                    "The selected camera exposes no valid active sensor array.");
        }
        int orientation = orientationValue == null ? 0 : orientationValue;
        if (orientation != 0 && orientation != 90 &&
                orientation != 180 && orientation != 270) {
            throw new IllegalStateException(
                    "The selected camera exposes an invalid sensor orientation: " +
                    orientation);
        }
        return new ReachyCameraDescriptor(
                selectedCameraId,
                facingLabel(facingValue),
                orientation,
                activeArrayValue,
                ReachyFrameIntrinsics.from(characteristics, activeArrayValue));
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
}
