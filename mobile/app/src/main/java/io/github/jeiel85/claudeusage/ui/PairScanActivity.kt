package io.github.jeiel85.claudeusage.ui

import android.os.Bundle
import com.journeyapps.barcodescanner.CaptureActivity
import com.journeyapps.barcodescanner.DecoratedBarcodeView

/**
 * zxing's scanner tuned for the pairing QR on a PC monitor (#251). That QR is dense (77–109 modules)
 * and sits as a white square on a dark window, so the defaults fight it: autofocus re-triggers only
 * every 2 s, so the picture stays blurred while the phone moves in, and whole-frame metering exposes
 * for the dark window and blooms the white square. Continuous focus and center metering fix both;
 * barcode scene mode is used where the camera has it.
 */
class PairScanActivity : CaptureActivity() {
    private lateinit var scanner: DecoratedBarcodeView

    override fun initializeContent(): DecoratedBarcodeView = super.initializeContent().also { scanner = it }

    override fun onCreate(savedInstanceState: Bundle?) {
        // super reads the scan intent and replaces the camera settings, so tune them afterwards;
        // the camera opens in onResume and picks these up then.
        super.onCreate(savedInstanceState)
        scanner.barcodeView.cameraSettings.apply {
            isContinuousFocusEnabled = true
            isMeteringEnabled = true
            isBarcodeSceneModeEnabled = true
        }
    }
}
