const { defineConfig } = require("cypress");

module.exports = defineConfig({
  allowCypressEnv: false,

  e2e: {
    baseUrl: "https://localhost:7244",
    pageLoadTimeout: 120000,

    setupNodeEvents(on, config) {
      // implement node event listeners here
    },
  },
});