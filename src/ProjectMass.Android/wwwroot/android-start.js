import { AndroidBarcodeDetector } from './android-barcode.js';

// Android WebView can expose BarcodeDetector without a working Google Play Services bridge.
globalThis.BarcodeDetector = AndroidBarcodeDetector;
