package com.ekkus93.weachy.camera;

import android.graphics.Rect;
import android.hardware.camera2.CameraCharacteristics;
import android.util.SizeF;

import org.json.JSONException;
import org.json.JSONObject;

final class ReachyFrameIntrinsics {
    final String source;
    final float focalLengthX;
    final float focalLengthY;
    final float principalPointX;
    final float principalPointY;
    final float skew;
    final String provenance;

    ReachyFrameIntrinsics(
            String source,
            float focalLengthX,
            float focalLengthY,
            float principalPointX,
            float principalPointY,
            float skew,
            String provenance) {
        this.source = source;
        this.focalLengthX = focalLengthX;
        this.focalLengthY = focalLengthY;
        this.principalPointX = principalPointX;
        this.principalPointY = principalPointY;
        this.skew = skew;
        this.provenance = provenance;
    }

    static ReachyFrameIntrinsics from(
            CameraCharacteristics characteristics,
            Rect activeArray) {
        float[] calibration = characteristics.get(
                CameraCharacteristics.LENS_INTRINSIC_CALIBRATION);
        if (calibration != null && calibration.length >= 5 &&
                calibration[0] > 0.0f && calibration[1] > 0.0f &&
                isFinite(calibration[0]) && isFinite(calibration[1]) &&
                isFinite(calibration[2]) && isFinite(calibration[3]) &&
                isFinite(calibration[4])) {
            return new ReachyFrameIntrinsics(
                    "android_calibration",
                    calibration[0],
                    calibration[1],
                    calibration[2],
                    calibration[3],
                    calibration[4],
                    "Camera2 LENS_INTRINSIC_CALIBRATION in active-sensor-array coordinates");
        }

        SizeF physicalSize = characteristics.get(
                CameraCharacteristics.SENSOR_INFO_PHYSICAL_SIZE);
        float[] focalLengths = characteristics.get(
                CameraCharacteristics.LENS_INFO_AVAILABLE_FOCAL_LENGTHS);
        if (physicalSize != null &&
                physicalSize.getWidth() > 0.0f &&
                physicalSize.getHeight() > 0.0f &&
                focalLengths != null && focalLengths.length > 0 &&
                focalLengths[0] > 0.0f && isFinite(focalLengths[0])) {
            float fx = focalLengths[0] *
                    activeArray.width() / physicalSize.getWidth();
            float fy = focalLengths[0] *
                    activeArray.height() / physicalSize.getHeight();
            return new ReachyFrameIntrinsics(
                    "uncalibrated_pinhole_estimate",
                    fx,
                    fy,
                    activeArray.exactCenterX(),
                    activeArray.exactCenterY(),
                    0.0f,
                    "Uncalibrated pinhole estimate from Camera2 physical sensor size, available focal length, and active sensor array");
        }

        return new ReachyFrameIntrinsics(
                "uncalibrated_pinhole_estimate",
                (float) activeArray.width(),
                (float) activeArray.height(),
                activeArray.exactCenterX(),
                activeArray.exactCenterY(),
                0.0f,
                "Uncalibrated normalized pinhole fallback derived only from the active sensor array; persist checkerboard calibration before metric reprojection");
    }

    JSONObject toJson(Rect activeArray) throws JSONException {
        JSONObject value = new JSONObject();
        value.put("source", source);
        value.put("fx", focalLengthX);
        value.put("fy", focalLengthY);
        value.put("cx", principalPointX);
        value.put("cy", principalPointY);
        value.put("skew", skew);
        value.put("coordinateSpace", "active_sensor_array");
        value.put("activeArrayLeft", activeArray.left);
        value.put("activeArrayTop", activeArray.top);
        value.put("activeArrayRight", activeArray.right);
        value.put("activeArrayBottom", activeArray.bottom);
        value.put("provenance", provenance);
        return value;
    }

    private static boolean isFinite(float value) {
        return !Float.isNaN(value) && !Float.isInfinite(value);
    }
}
