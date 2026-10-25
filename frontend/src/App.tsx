import { useState } from "react";
import { createBrowserRouter, RouterProvider, type RouteObject } from "react-router";
import { DashboardScreen } from "./features/dashboard/DashboardScreen";
import { GovernanceScreen } from "./features/governance/GovernanceScreen";
import { IntelligenceScreen } from "./features/intelligence/IntelligenceScreen";
import { KnowledgeScreen } from "./features/knowledge/KnowledgeScreen";
import { RecommendationsScreen } from "./features/recommendations/RecommendationsScreen";
import { Layout } from "./shell/Layout";
import { NotFound } from "./shell/NotFound";

// The one route table: every screen is a child of the shell's layout. Navigation.tsx lists the areas.
export const routes: RouteObject[] = [
  {
    element: <Layout />,
    children: [
      { index: true, element: <DashboardScreen /> },
      { path: "intelligence", element: <IntelligenceScreen /> },
      { path: "recommendations", element: <RecommendationsScreen /> },
      { path: "knowledge", element: <KnowledgeScreen /> },
      { path: "governance", element: <GovernanceScreen /> },
      { path: "*", element: <NotFound /> },
    ],
  },
];

export function App() {
  const [router] = useState(() => createBrowserRouter(routes));
  return <RouterProvider router={router} />;
}
