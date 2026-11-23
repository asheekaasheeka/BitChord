import { QueryClientProvider } from '@tanstack/react-query';
import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { createBrowserRouter, RouterProvider } from 'react-router';
import { queryClient } from '@/lib/queryClient';
import { routes } from '@/routes';
import '@/styles/global.css';

const router = createBrowserRouter(routes);

// Inside the Windows app only: karaoke collects each song's segments from the first one on.
if ((window as { chrome?: { webview?: unknown } }).chrome?.webview) {
  void import('@/playback/karaoke').then((m) => m.installKaraoke());
}

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <QueryClientProvider client={queryClient}>
      <RouterProvider router={router} />
    </QueryClientProvider>
  </StrictMode>,
);
