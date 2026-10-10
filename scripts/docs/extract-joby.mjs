import { discoverServices, extractArchitecture } from "./extract-architecture.mjs";
import { indexClasses } from "./csharp.mjs";
import { extractFrontend } from "./extract-frontend.mjs";
import { extractIntegrations } from "./extract-integrations.mjs";
import { extractJobs } from "./extract-jobs.mjs";

export function collectRuntime(root) {
  const architecture = extractArchitecture(root);
  const services = discoverServices(root);
  const classes = indexClasses(services);
  const { jobs, triggers } = extractJobs(services, classes);
  const integrations = extractIntegrations(services, classes, jobs);
  const { routes, uiCalls } = extractFrontend(root, architecture);
  return { architecture, jobs, triggers, integrations, routes, uiCalls };
}

export function extractJoby(root) {
  const { jobs, triggers, integrations, routes } = collectRuntime(root);
  return { jobs, triggers, integrations, routes };
}
