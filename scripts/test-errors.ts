#!/usr/bin/env bun

import { DOMParser } from "@xmldom/xmldom";
import { join } from "node:path";
import { stringify } from 'yaml';
import { parseArgs } from "util";

const { values } = parseArgs({
  args: Bun.argv,
  options: {
    SummaryOnly: {
      type: "boolean",
    },
  },
  strict: false,
  allowPositionals: true,
});

const summaryOnly = values.SummaryOnly ?? false;

const solutionDir = join(__dirname, "../");
const testresultsDir = join(solutionDir, ".testresults");
const trxFilePath = join(testresultsDir, "results.trx");

const trxFile = Bun.file(trxFilePath);

// Check file exists
if (!(await trxFile.exists())) {
  console.error(`Error: Test results file not found: ${trxFile}`);
  process.exit(1);
}

// Read and parse XML
const xmlContent = await trxFile.text();
const parser = new DOMParser();
const doc = parser.parseFromString(xmlContent, "text/xml");

// Get result summary
const resultSummary = doc.getElementsByTagName("ResultSummary")[0];
if (!resultSummary) {
  console.error("Error: Invalid TRX file - missing ResultSummary");
  process.exit(1);
}

const outcome = resultSummary.getAttribute("outcome");
const counters = resultSummary.getElementsByTagName("Counters")[0];

if (!counters) {
  console.error("Error: Invalid TRX file - missing Counters");
  process.exit(1);
}

// Parse counters
const total = counters.getAttribute("total");
const passed = counters.getAttribute("passed");
const failed = counters.getAttribute("failed");
const error = counters.getAttribute("error");

// Print summary

interface SuccessReport {
  status: "ok";
  total: number;
  passed: number;
}

// If success, exit
if (outcome === "Completed") {
  
  const report: SuccessReport = {
    status: "ok",
    total: Number(total),
    passed: Number(passed),
  }
  console.log("---\n" + stringify(report, {}));
  process.exit(0);
}

interface Failure {
  fqTestName: string;
  errorMessage: string;
}

// Find failed tests
const results = doc.getElementsByTagName("UnitTestResult");
const testDefinitions = doc.getElementsByTagName("UnitTest");
const failures: Array<Failure> = [];

for (let i = 0; i < results.length; i++) {
  const result = results[i];
  if (result?.getAttribute("outcome") === "Failed") {
    const testId = result.getAttribute("testId");

    // Find matching test definition
    let testDef: Element | undefined = undefined;
    for (let j = 0; j < testDefinitions.length; j++) {
      if (testDefinitions[j]?.getAttribute("id") === testId) {
        testDef = testDefinitions[j];
        break;
      }
    }

    if (testDef) {
      const testMethod = testDef.getElementsByTagName("TestMethod")[0];
      const className = testMethod?.getAttribute("className") || "Unknown";
      const methodName = testMethod?.getAttribute("name") || "Unknown";
      const fqTestName = `${className}.${methodName}`;

      // Extract error message
      const output = result.getElementsByTagName("Output")[0];
      const errorInfo = output?.getElementsByTagName("ErrorInfo")[0];
      const message = errorInfo?.getElementsByTagName("Message")[0];
      const errorMessage = message?.textContent?.trim() || "No error message";

      failures.push({ fqTestName, errorMessage });
    }
  }
}

interface ErrorReport {
  status: "error";
  failed: number;
  total: number;
  passed: number;
  tests?: {
    method: string;
    message: string;
  }[];
}

// Print failures
if (failures.length > 0) {

  const report: ErrorReport = {
    status: "error",
    failed: failures.length,
    passed: Number(passed),
    total: Number(total),
  };

  // Only include detailed test info if not in summary-only mode
  if (!summaryOnly) {
    report.tests = failures.map(f => ({
      method: f.fqTestName,
      message: f.errorMessage.replace(/\r?\n/g, "\n  "),
    }));
  }

  console.error("---\n" + stringify(report, {}));

  process.exit(1);
}

process.exit(0);
