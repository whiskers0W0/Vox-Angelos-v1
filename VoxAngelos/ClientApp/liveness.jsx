import React, { useEffect, useState } from 'react';
import { createRoot } from 'react-dom/client';
import { FaceLivenessDetector } from '@aws-amplify/ui-react-liveness';
import { fromCognitoIdentityPool } from '@aws-sdk/credential-provider-cognito-identity';
import { CognitoIdentityClient } from '@aws-sdk/client-cognito-identity';
import '@aws-amplify/ui-react-liveness/styles.css';

function LivenessApp({ config }) {
  const [sessionId, setSessionId] = useState(null);
  const [error, setError] = useState(null);

  useEffect(() => {
    window.addEventListener('vox:start-liveness', async () => {
      setError(null);
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
    onAnalysisComplete={() => {
      window.dispatchEvent(new CustomEvent('vox:liveness-complete', { detail: { sessionId } }));
      window.dispatchEvent(new Event('vox:liveness-ended'));
      setSessionId(null);
    }}
    onError={(e) => {
      setError(e?.message || 'Liveness check failed. Please retry.');
      window.dispatchEvent(new Event('vox:liveness-ended'));
    }}
  />;
}

const host = document.getElementById('awsLivenessRoot');
if (host) createRoot(host).render(<LivenessApp config={JSON.parse(host.dataset.config)} />);
