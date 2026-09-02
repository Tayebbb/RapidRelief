// RapidRelief — Auth Helper for Neon Google OAuth & Session Retrieval
window.rapidReliefAuth = {
    getNeonSession: async function() {
        try {
            const resp = await fetch('https://ep-little-mountain-b3ttfx56.neonauth.c-4.ap-southeast-1.aws.neon.tech/neondb/auth/get-session', {
                credentials: 'include',
                headers: { 'Accept': 'application/json' }
            });
            if (resp.ok) {
                const data = await resp.json();
                if (data && data.user) {
                    return JSON.stringify(data.user);
                }
            }
        } catch (e) {
            console.warn('Neon auth session query:', e);
        }
        return null;
    }
};
