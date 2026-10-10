const slug = (value) => value.toLowerCase().replace(/[^a-z0-9]+/g, "-").replace(/^-|-$/g, "");

export const endpointAnchor = (verb, route) => `ep-${verb.toLowerCase()}-${slug(route)}`;
export const messageAnchor = (name) => `msg-${name}`;
export const tableAnchor = (service, table) => `table-${service}-${table}`;
export const jobAnchor = (key) => `job-${slug(key)}`;
export const integrationAnchor = (client) => `integration-${client}`;
export const adrAnchor = (id) => `adr-${id.replace(/^ADR-/i, "")}`;
