// Shared SignalR connection to /hubs/feed. Individual pages set
// window.VoxAngelosRealtime may provide concern, recommendation, publish, or rating handlers.
// (any of the three) in their own @section Scripts *before* this file's handlers fire —
// they only need to exist by the time the corresponding event actually arrives, not at
// page-load instant, since broadcasts only happen when another user takes an action.
(function () {
    if (typeof signalR === 'undefined') return;

    const connection = new signalR.HubConnectionBuilder()
        .withUrl('/hubs/feed')
        .withAutomaticReconnect()
        .build();

    connection.on('ConcernFeedChanged', () => {
        window.VoxAngelosRealtime?.onConcernFeedChanged?.();
        window.dispatchEvent(new CustomEvent('voxangelos:lgu-notification', {
            detail: { type: 'concern' }
        }));
    });

    connection.on('RecommendationFeedChanged', () => {
        window.VoxAngelosRealtime?.onRecommendationFeedChanged?.();
        window.dispatchEvent(new CustomEvent('voxangelos:lgu-notification', {
            detail: { type: 'recommendation' }
        }));
    });

    connection.on('PostPublished', () => {
        window.VoxAngelosRealtime?.onPostPublished?.();
    });

    connection.on('RatingUpdated', (payload) => {
        window.VoxAngelosRealtime?.onRatingUpdated?.(payload);
    });

    connection.start().catch(err => console.error('Realtime feed connection failed:', err));

    window.VoxAngelosFeedConnection = connection;
})();
