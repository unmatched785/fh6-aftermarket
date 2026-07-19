import { readFile, writeFile } from "node:fs/promises";
import path from "node:path";

const publicCarListUrl = "https://fh6meta.com/car-list?lang=en";
const argumentsByName = new Map();

for (let index = 2; index < process.argv.length; index += 2) {
  const name = process.argv[index];
  const value = process.argv[index + 1];
  if (!name?.startsWith("--") || !value) {
    throw new Error(`Invalid argument near: ${name ?? "<end>"}`);
  }

  argumentsByName.set(name, value);
}

const inputPath = argumentsByName.get("--input");
const outputPath = path.resolve(
  argumentsByName.get("--output") ?? "config/official-cars.json"
);

const html = inputPath
  ? await readFile(path.resolve(inputPath), "utf8")
  : await fetchPublicCarList();
const dataset = extractDataset(html);
const cars = dataset.cars.map((car) => ({
  id: requiredString(car.id, "id"),
  year: requiredString(car.year, "year"),
  make: requiredString(car.make, "make"),
  model: requiredString(car.model, "model"),
  carName: requiredString(car.carName, "carName")
}));

validate(dataset, cars);

const catalog = {
  sourceId: "fh6meta-live-car-list",
  sourceName: "FH6 Meta live car list",
  source: publicCarListUrl,
  upstreamSource: "https://forza.net/fh6cars",
  fetchedAt: requiredString(dataset.sourceStatus?.officialCars, "sourceStatus.officialCars"),
  publishedAt: requiredString(dataset.generatedAt, "generatedAt"),
  count: cars.length,
  cars
};

await writeFile(outputPath, `${JSON.stringify(catalog, null, 2)}\n`, "utf8");

console.log(`Updated ${outputPath}`);
console.log(`FH6 Meta source fetched: ${catalog.fetchedAt}`);
console.log(`Published dataset: ${catalog.publishedAt}`);
console.log(`Official cars: ${catalog.count}`);

async function fetchPublicCarList() {
  const response = await fetch(publicCarListUrl, {
    headers: { "User-Agent": "fh6-aftermarket-car-db-updater/1.0" }
  });
  if (!response.ok) {
    throw new Error(`FH6 Meta returned HTTP ${response.status}.`);
  }

  return response.text();
}

function extractDataset(pageHtml) {
  const chunks = [];
  const pushPattern = /self\.__next_f\.push\((.*?)\)<\/script>/gs;

  for (const match of pageHtml.matchAll(pushPattern)) {
    let payload;
    try {
      payload = JSON.parse(match[1]);
    } catch {
      continue;
    }

    if (payload[0] === 1 && typeof payload[1] === "string") {
      chunks.push(payload[1]);
    }
  }

  const flightData = chunks.join("");
  const marker = '"dataset":';
  const markerIndex = flightData.indexOf(marker);
  if (markerIndex < 0) {
    throw new Error("FH6 Meta page did not contain a serialized car-list dataset.");
  }

  const objectStart = flightData.indexOf("{", markerIndex + marker.length);
  const objectEnd = findJsonObjectEnd(flightData, objectStart);
  return JSON.parse(flightData.slice(objectStart, objectEnd + 1));
}

function findJsonObjectEnd(value, objectStart) {
  if (objectStart < 0 || value[objectStart] !== "{") {
    throw new Error("Serialized dataset object was not found.");
  }

  let depth = 0;
  let inString = false;
  let escaped = false;

  for (let index = objectStart; index < value.length; index++) {
    const character = value[index];
    if (inString) {
      if (escaped) {
        escaped = false;
      } else if (character === "\\") {
        escaped = true;
      } else if (character === '"') {
        inString = false;
      }
      continue;
    }

    if (character === '"') {
      inString = true;
    } else if (character === "{") {
      depth++;
    } else if (character === "}") {
      depth--;
      if (depth === 0) {
        return index;
      }
    }
  }

  throw new Error("Serialized dataset object was incomplete.");
}

function requiredString(value, field) {
  if (typeof value !== "string" || value.length === 0) {
    throw new Error(`Missing required field: ${field}`);
  }

  return value;
}

function validate(dataset, cars) {
  if (!Array.isArray(dataset.cars) || cars.length < 600) {
    throw new Error(`Expected at least 600 official cars, received ${cars.length}.`);
  }

  if (dataset.counts?.officialCars !== cars.length) {
    throw new Error(
      `FH6 Meta count mismatch: declared=${dataset.counts?.officialCars}, actual=${cars.length}.`
    );
  }

  const ids = new Set(cars.map((car) => car.id));
  if (ids.size !== cars.length) {
    throw new Error("FH6 Meta car list contains duplicate IDs.");
  }

  const requiredTargetIds = [
    "1984-ferrari-288-gto",
    "2012-ferrari-599xx-evolution",
    "2019-ferrari-f8-tributo",
    "2012-lamborghini-aventador-lp700-4",
    "2011-lamborghini-sesto-elemento",
    "1999-lamborghini-diablo-gtr"
  ];
  const missingTargets = requiredTargetIds.filter((id) => !ids.has(id));
  if (missingTargets.length > 0) {
    throw new Error(`FH6 Meta car list is missing target IDs: ${missingTargets.join(", ")}`);
  }
}
