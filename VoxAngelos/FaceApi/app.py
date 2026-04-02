from flask import Flask, request, jsonify
from flask_cors import CORS
import face_recognition
import cv2
import numpy as np
import os
import uuid
from PIL import Image
import re
from datetime import datetime

print("✅ BACKEND STARTING ON PORT 5051")

app = Flask(__name__)

CORS(app, origins=["https://localhost:7244", "http://localhost:7244"], supports_credentials=True)

UPLOAD_FOLDER = "temp_uploads"
os.makedirs(UPLOAD_FOLDER, exist_ok=True)

DISTANCE_THRESHOLD = 0.55  # stricter than default 0.6

try:
    import pytesseract
    pytesseract.pytesseract.tesseract_cmd = r'C:\Program Files\Tesseract-OCR\tesseract.exe'
    OCR_AVAILABLE = True
except ImportError:
    OCR_AVAILABLE = False

# --- LOCALITY CONFIGURATION ---
# Set this to "DINALUPIHAN" for your local testing, "ANGELES" for production
CURRENT_TESTING_MODE = "DINALUPIHAN" 

ANGELES_BARANGAYS = [
    "AGAPITO DEL ROSARIO", "ANUNAS", "BALIBAGO", "BANA BANA", "CUTCUT", 
    "LOURDES SUR", "LOURDES SUR EAST", "MALABANIAS", "MARGOT", "MINING", 
    "PAMPANG", "PANDAN", "PULUNG BULU", "PULUNG MARAGUL", "PULUNG CACUTUD", 
    "SALAPUNG", "SAN JOSE", "SAN NICOLAS", "SANTA TERESITA", "SANTA TRINIDAD", 
    "SANTO DOMINGO", "SANTO ROSARIO", "SAPA LIBUTAD", "SAPANGBATO", "TABUN"
]

DINALUPIHAN_BARANGAYS = [
    "AQUINO", "BANGAL", "BATAWAN", "BAYAN-BAYANAN", "BONIFACIO", "COLO", 
    "DAANG BAGO", "DALAO", "GEN. LUNA", "GOMEZ", "HAPPY VALLEY", "LAYAC", 
    "LUACAN", "MABINI EXT.", "MAGSAYSAY", "MALIGAYA", "NAPERING", "PAG-ASA", 
    "PAGALANGGANG", "PENTOR", "PINULOT", "PITA", "RIVERSIDE", "ROOSEVELT", 
    "SAGUING", "SAN BENITO", "SAN ISIDRO", "SAN JOSE", "SAN RAMON", "SANTA ISABEL", 
    "STO. NIÑO", "TOWNSITE", "TUCOP", "ZAMORA"
]

ACTIVE_BARANGAYS = DINALUPIHAN_BARANGAYS if CURRENT_TESTING_MODE == "DINALUPIHAN" else ANGELES_BARANGAYS

@app.route("/ocr-id", methods=["POST"])
def ocr_id():
    temp_path = None
    try:
        if "idPhoto" not in request.files:
            return jsonify({"success": False, "error": "No ID uploaded."}), 400

        temp_path = os.path.join(UPLOAD_FOLDER, f"{uuid.uuid4()}_ocr.jpg")
        if not normalize_image(request.files["idPhoto"], temp_path):
            return jsonify({"success": False, "error": "Invalid image format."}), 400

        if not OCR_AVAILABLE:
            return jsonify({
                "success": True,
                "rawFullText": "",
                "detectedBirthDate": None,
                "detectedAddress": None,
                "detectedLocality": None,
                "localityMatched": False,
                "ocrConfidence": 0.0,
                "detectionType": "unavailable",
                "detectedLanguageCode": "en"
            })

        img = cv2.imread(temp_path)
        # Pre-processing for better OCR accuracy
        gray = cv2.cvtColor(img, cv2.COLOR_BGR2GRAY)
        _, thresh = cv2.threshold(gray, 0, 255, cv2.THRESH_BINARY + cv2.THRESH_OTSU)

        raw_text = pytesseract.image_to_string(thresh, lang='eng')
        text_upper = raw_text.upper()
        raw_text_clean = ' '.join(raw_text.split())

        # ── 1. Extract Birth Date ───────────────────────────────
        birth_date = None
        date_patterns = [
            r'\b(\d{4})[\/\-](\d{2})[\/\-](\d{2})\b',
            r'\b(\d{2})[\/\-](\d{2})[\/\-](\d{4})\b',
            r'\b(JAN|FEB|MAR|APR|MAY|JUN|JUL|AUG|SEP|OCT|NOV|DEC)[\.\/\s]+(\d{1,2})[,\s]+(\d{4})\b'
        ]
        for pattern in date_patterns:
            match = re.search(pattern, text_upper)
            if match:
                birth_date = match.group(0)
                break

        # ── 2. Extract Full Address ─────────────────────────────
        detected_address = "Not detected"
        lines = [line.strip() for line in text_upper.split('\n') if line.strip()]
        
        for i, line in enumerate(lines):
            if 'ADDRESS' in line:
                address_parts = []
                # Remove the label 'ADDRESS' from the first line
                first_line = line.replace('ADDRESS', '').strip()
                if first_line: address_parts.append(first_line)
                
                # Capture the next 3 lines or until a field separator is found
                # (Driver's licenses usually follow Address with Weight, Sex, or License No)
                for j in range(i + 1, min(i + 4, len(lines))):
                    if any(k in lines[j] for k in ['SEX', 'WEIGHT', 'HEIGHT', 'NATIONALITY', 'DATE', 'EXPIRY']):
                        break
                    address_parts.append(lines[j])
                
                detected_address = " ".join(address_parts).strip()
                break

        # ── 3. Extract Barangay (Locality) ──────────────────────
        detected_locality = "Not detected"
        locality_matched = False
        
        # Check if any Angeles Barangay exists in the full raw text
        for bgy in ACTIVE_BARANGAYS:
            pattern = r'\b' + re.escape(bgy.upper()) + r'\b'
            if re.search(pattern, text_upper):
                detected_locality = bgy.title()
                locality_matched = True
                break

        # ── 4. OCR Confidence ───────────────────────────────────
        data = pytesseract.image_to_data(thresh, output_type=pytesseract.Output.DICT)
        confidences = [int(c) for c in data['conf'] if str(c).isdigit() and int(c) > 0]
        avg_confidence = round(sum(confidences) / len(confidences) / 100, 4) if confidences else 0.0

        return jsonify({
            "success": True,
            "rawFullText": raw_text_clean,
            "detectedBirthDate": birth_date,
            "detectedAddress": detected_address,
            "detectedLocality": detected_locality,
            "localityMatched": locality_matched,
            "ocrConfidence": float(avg_confidence),
            "detectionType": "tesseract",
            "detectedLanguageCode": "en"
        })

    except Exception as ex:
        print(f"OCR Error: {ex}")
        return jsonify({"success": False, "error": str(ex)}), 500
    finally:
        if temp_path and os.path.exists(temp_path):
            os.remove(temp_path)

def normalize_image(file_storage, output_path):
    try:
        with Image.open(file_storage) as img:
            img = img.convert("RGB")
            img.save(output_path, "JPEG", quality=95)
        return True
    except Exception as e:
        print(f"Normalize error: {e}")
        return False

def get_face_encoding(img_path):
    try:
        image = face_recognition.load_image_file(img_path)
        encodings = face_recognition.face_encodings(image)
        if len(encodings) == 0:
            return None, "No face detected."
        if len(encodings) > 1:
            return None, "Multiple faces detected."
        return encodings[0], None
    except Exception as e:
        return None, str(e)

@app.route("/verify", methods=["POST"])
def verify():
    id_path = None
    selfie_path = None
    try:
        if "idPhoto" not in request.files or "selfie" not in request.files:
            return jsonify({"error": "Missing files"}), 400

        id_path = os.path.join(UPLOAD_FOLDER, f"{uuid.uuid4()}_id.jpg")
        selfie_path = os.path.join(UPLOAD_FOLDER, f"{uuid.uuid4()}_selfie.jpg")

        if not normalize_image(request.files["idPhoto"], id_path) or \
           not normalize_image(request.files["selfie"], selfie_path):
            return jsonify({"isMatch": False, "error": "Image processing failed."})

        id_enc, id_err = get_face_encoding(id_path)
        selfie_enc, self_err = get_face_encoding(selfie_path)

        if id_enc is None:
            return jsonify({"isMatch": False, "error": f"ID: {id_err}"})
        if selfie_enc is None:
            return jsonify({"isMatch": False, "error": f"Selfie: {self_err}"})

        distance = np.linalg.norm(id_enc - selfie_enc)
        is_match = bool(distance < DISTANCE_THRESHOLD)

        # Correct confidence: 1.0 = perfect match, 0.0 = completely different
        confidence = round(max(0.0, 1.0 - float(distance)), 4)

        return jsonify({
            "isMatch": is_match,
            "confidence": confidence,
            "distance": float(distance)
        })
    finally:
        for p in [id_path, selfie_path]:
            if p and os.path.exists(p):
                os.remove(p)

@app.route("/validate-id", methods=["POST"])
def validate_id():
    temp_path = None
    try:
        if "idPhoto" not in request.files:
            return jsonify({"isValidId": False, "reason": "No ID uploaded."}), 400

        temp_path = os.path.join(UPLOAD_FOLDER, f"{uuid.uuid4()}_val.jpg")
        if not normalize_image(request.files["idPhoto"], temp_path):
            return jsonify({"isValidId": False, "reason": "Invalid format."})

        img = cv2.imread(temp_path)
        h, w = img.shape[:2]

        if w < 300 or h < 200:
            return jsonify({"isValidId": False, "reason": "Resolution too low."})

        image_rgb = face_recognition.load_image_file(temp_path)
        faces = face_recognition.face_locations(image_rgb)

        if not faces:
            return jsonify({"isValidId": False, "reason": "No face detected."})

        gray = cv2.cvtColor(img, cv2.COLOR_BGR2GRAY)
        blur = cv2.Laplacian(gray, cv2.CV_64F).var()
        if blur < 30:
            return jsonify({"isValidId": False, "reason": "Image too blurry."})

        return jsonify({"isValidId": True, "reason": "Valid ID"})
    finally:
        if temp_path and os.path.exists(temp_path):
            os.remove(temp_path)

if __name__ == "__main__":
    app.run(port=5051, debug=True)