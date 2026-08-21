import React, { useEffect, useState } from 'react';
import { createRoot } from 'react-dom/client';
import { FaceLivenessDetector } from '@aws-amplify/ui-react-liveness';
import { fromCognitoIdentityPool } from '@aws-sdk/credential-provider-cognito-identity';
import { CognitoIdentityClient } from '@aws-sdk/client-cognito-identity';
import '@aws-amplify/ui-react-liveness/styles.css';

// Vox Angelos does not use the optional flashing-light start-screen treatment.
// Supplying an empty supported component keeps the AWS detector behavior intact
// while omitting its generic photosensitivity notice.
const NoPhotosensitivityWarning = () => null;

function friendlyLivenessError(error) {
  const message = String(error?.message || '').toLowerCase();
  if (message.includes('camera') || message.includes('permission')) {
    return 'We could not access your camera. Allow camera permission, close other apps using the camera, and try again.';
  }
  if (message.includes('timeout') || message.includes('timed out')) {
    return 'The face check took too long. Use a stable connection, keep your face centered and well lit, then try again.';
  }
  if (message.includes('multiple') || message.includes('more than one')) {
    return 'More than one face was detected. Make sure you are the only person visible, then try again.';
  }
  return 'We could not complete the live face check. Keep your whole face visible, remove masks, hats, and sunglasses, use even lighting, and try again.';
}

function LivenessApp({ config }) {
  const [sessionId, setSessionId] = useState(null);
  const [error, setError] = useState(null);

  const releaseActiveSession = async () => {
    try {
      await fetch('?handler=CancelFaceLiveness', {
        method: 'POST',
        headers: { RequestVerificationToken: document.querySelector('input[name="__RequestVerificationToken"]').value }
      });
    } catch {
      // The server lock also expires automatically; cancellation must never
      // replace the useful camera/liveness error shown to the citizen.
    }
  };

  useEffect(() => {
    window.addEventListener('vox:start-liveness', async () => {
      setError(null);
      setSessionId(null);
      try {
        const response = await fetch('?handler=StartFaceLiveness', {
          method: 'POST',
          headers: { RequestVerificationToken: document.querySelector('input[name="__RequestVerificationToken"]').value }
        });
        const body = await response.json();
        if (!response.ok || !body.success) throw new Error(body.error || 'Unable to start liveness check.');
        setSessionId(body.sessionId);
        window.dispatchEvent(new Event('vox:liveness-started'));
      } catch (e) {
        setError(e.message);
        window.dispatchEvent(new Event('vox:liveness-ended'));
      }
    });
  }, []);

  useEffect(() => {
    if (!sessionId) {
      host.removeAttribute('data-guidance');
      return;
    }

    const updateGuidanceColor = () => {
      const message = host.querySelector('.amplify-liveness-hint__text')?.textContent?.trim().toLowerCase() || '';
      let guidance = 'adjust';

      if (/face detected|hold still|verifying|check complete|lighting conditions normal/.test(message)) {
        guidance = 'ready';
      } else if (/error|multiple faces|only one face/.test(message)) {
        guidance = 'error';
      }

      host.dataset.guidance = guidance;
    };

    const observer = new MutationObserver(updateGuidanceColor);
    observer.observe(host, { subtree: true, childList: true, characterData: true });
    updateGuidanceColor();
    return () => observer.disconnect();
  }, [sessionId]);

  if (error) return <div className="selfie-status error">{error}</div>;
  if (!sessionId) return null;

  const credentialsProvider = fromCognitoIdentityPool({
    client: new CognitoIdentityClient({ region: config.region }),
    identityPoolId: config.identityPoolId
  });

  return <FaceLivenessDetector
    sessionId={sessionId}
    region={config.region}
    config={{ credentialProvider: credentialsProvider }}
    components={{ PhotosensitiveWarning: NoPhotosensitivityWarning }}
    onAnalysisComplete={() => {
      window.dispatchEvent(new CustomEvent('vox:liveness-complete', { detail: { sessionId } }));
      window.dispatchEvent(new Event('vox:liveness-ended'));
      setSessionId(null);
    }}
    onError={async (e) => {
      // Discard this AWS session before allowing a retry. Failed sessions never
      // create a registration ticket and their reference image is never reused.
      setSessionId(null);
      await releaseActiveSession();
      setError(friendlyLivenessError(e));
      window.dispatchEvent(new Event('vox:liveness-ended'));
    }}
    onUserCancel={async () => {
      setSessionId(null);
      await releaseActiveSession();
      setError('Face check cancelled. You can start a new check when you are ready.');
      window.dispatchEvent(new Event('vox:liveness-ended'));
    }}
  />;
}

const host = document.getElementById('awsLivenessRoot');
if (host) createRoot(host).render(<LivenessApp config={JSON.parse(host.dataset.config)} />);
