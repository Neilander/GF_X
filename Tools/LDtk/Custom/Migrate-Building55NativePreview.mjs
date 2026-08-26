import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const projectRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "../../..");
const ldtkRoot = path.join(projectRoot, "Assets/AAAGame/Tilemap/Ldtk");
const projectPath = path.join(ldtkRoot, "City.ldtk");
const levelRoot = path.join(ldtkRoot, "City");
const buildingIdentifiers = new Set(["Building22", "Building33", "Building44", "Building55"]);
const expectedSizes = new Map([
    ["Building22", 32],
    ["Building33", 48],
    ["Building44", 64],
    ["Building55", 80],
]);
const previewIdentifiers = new Set([
    "Building22Preview",
    "Building33Preview",
    "Building44Preview",
]);

function detectNewline(text) {
    return text.includes("\r\n") ? "\r\n" : "\n";
}

function stringifyAtIndent(value, indent, newline) {
    return JSON.stringify(value, null, "\t")
        .split("\n")
        .map(line => indent + line)
        .join(newline);
}

function findMatchingBrace(text, start) {
    let depth = 0;
    let inString = false;
    let escaped = false;
    for (let index = start; index < text.length; index++) {
        const character = text[index];
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
                return index + 1;
            }
        }
    }
    throw new Error(`Unterminated JSON object at offset ${start}.`);
}

function findObjectSpans(text, identifierKey, acceptedIdentifiers) {
    const escapedKey = identifierKey.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
    const expression = new RegExp(`^(\\s*)\\{\\r?\\n\\1\\t"${escapedKey}": "([^"]+)"`, "gm");
    const spans = [];
    let match;
    while ((match = expression.exec(text)) !== null) {
        if (!acceptedIdentifiers.has(match[2])) {
            continue;
        }
        const start = match.index;
        spans.push({
            start,
            end: findMatchingBrace(text, start),
            indent: match[1],
            identifier: match[2],
        });
    }
    return spans;
}

function findTilesetSpans(text, acceptedIdentifiers) {
    const expression = /^(\s*)\{\r?\n\1\t"__cWid": [^\r\n]+,\r?\n\1\t"__cHei": [^\r\n]+,\r?\n\1\t"identifier": "([^"]+)"/gm;
    const spans = [];
    let match;
    while ((match = expression.exec(text)) !== null) {
        if (!acceptedIdentifiers.has(match[2])) {
            continue;
        }
        const start = match.index;
        spans.push({
            start,
            end: findMatchingBrace(text, start),
            indent: match[1],
            identifier: match[2],
        });
    }
    return spans;
}

function replaceSpans(text, spans, transform, newline) {
    let result = text;
    for (const span of [...spans].sort((left, right) => right.start - left.start)) {
        const value = JSON.parse(result.slice(span.start, span.end));
        const replacement = stringifyAtIndent(transform(value), span.indent, newline);
        result = result.slice(0, span.start) + replacement + result.slice(span.end);
    }
    return result;
}

function removeSpansWithComma(text, spans) {
    let result = text;
    for (const span of [...spans].sort((left, right) => right.start - left.start)) {
        let start = span.start;
        let end = span.end;
        let cursor = end;
        while (cursor < result.length && /[ \t]/.test(result[cursor])) cursor++;
        if (result[cursor] === ",") {
            end = cursor + 1;
        } else {
            cursor = start - 1;
            while (cursor >= 0 && /[ \t\r\n]/.test(result[cursor])) cursor--;
            if (result[cursor] !== ",") {
                throw new Error(`Cannot remove JSON object without adjacent comma at offset ${span.start}.`);
            }
            start = cursor;
        }
        result = result.slice(0, start) + result.slice(end);
    }
    return result;
}

function configureNativeBuildingDefinition(definition) {
    definition.renderMode = "Rectangle";
    definition.tilesetId = null;
    definition.tileRenderMode = "FitInside";
    definition.tileRect = null;
    definition.uiTileRect = null;
    definition.tileOpacity = 1;
    definition.fillOpacity = 1;
    definition.lineOpacity = 1;
    return definition;
}

function createBuilding55Definition(building44) {
    const definition = structuredClone(building44);
    definition.identifier = "Building55";
    definition.uid = 65;
    definition.width = 80;
    definition.height = 80;
    definition.pivotSnapOffsetX = false;
    definition.pivotSnapOffsetY = false;
    definition.fieldDefs[0].uid = 66;
    definition.fieldDefs[1].uid = 67;
    definition.fieldDefs[2].uid = 68;
    return configureNativeBuildingDefinition(definition);
}

function migrateProject() {
    const original = fs.readFileSync(projectPath, "utf8");
    const newline = detectNewline(original);
    const parsed = JSON.parse(original);
    const definitions = parsed.defs.entities.filter(definition => buildingIdentifiers.has(definition.identifier));
    if (definitions.some(definition => definition.identifier === "Building55")) {
        throw new Error("City.ldtk already contains Building55; migration must start from the pre-migration project.");
    }
    if (parsed.nextUid !== 65) {
        throw new Error(`City.ldtk nextUid must be 65 before migration, actual=${parsed.nextUid}.`);
    }
    if (definitions.length !== 3) {
        throw new Error(`Expected exactly three legacy building definitions, actual=${definitions.length}.`);
    }

    const definitionSpans = findObjectSpans(original, "identifier", new Set(["Building22", "Building33", "Building44"]));
    if (definitionSpans.length !== 3) {
        throw new Error(`Expected three building definition spans, actual=${definitionSpans.length}.`);
    }
    let migrated = replaceSpans(original, definitionSpans, configureNativeBuildingDefinition, newline);

    const refreshedDefinitionSpans = findObjectSpans(migrated, "identifier", new Set(["Building44"]));
    const building44 = JSON.parse(migrated.slice(refreshedDefinitionSpans[0].start, refreshedDefinitionSpans[0].end));
    const building55 = stringifyAtIndent(createBuilding55Definition(building44), refreshedDefinitionSpans[0].indent, newline);
    migrated = migrated.slice(0, refreshedDefinitionSpans[0].end) + "," + newline + building55 + migrated.slice(refreshedDefinitionSpans[0].end);

    const previewSpans = findTilesetSpans(migrated, previewIdentifiers);
    if (previewSpans.length !== 3) {
        throw new Error(`Expected three building preview tilesets, actual=${previewSpans.length}.`);
    }
    migrated = removeSpansWithComma(migrated, previewSpans);
    migrated = migrated.replace('"nextUid": 65', '"nextUid": 69');

    const result = JSON.parse(migrated);
    validateProject(result);
    return migrated;
}

function migrateLevel(levelPath) {
    const original = fs.readFileSync(levelPath, "utf8");
    const newline = detectNewline(original);
    const spans = findObjectSpans(original, "__identifier", buildingIdentifiers);
    let migratedCoreCount = 0;
    const migrated = replaceSpans(original, spans, entity => {
        entity.__tile = null;
        if (entity.__identifier !== "Building44") {
            return entity;
        }

        migratedCoreCount++;
        entity.__identifier = "Building55";
        entity.width = 80;
        entity.height = 80;
        entity.defUid = 65;
        entity.px[0] += 8;
        entity.px[1] += 8;
        if (Number.isInteger(entity.__worldX)) entity.__worldX += 8;
        if (Number.isInteger(entity.__worldY)) entity.__worldY += 8;
        const fieldUids = [66, 67, 68];
        if (entity.fieldInstances.length !== fieldUids.length) {
            throw new Error(`${path.basename(levelPath)} ${entity.iid} has unexpected field count ${entity.fieldInstances.length}.`);
        }
        entity.fieldInstances.forEach((field, index) => field.defUid = fieldUids[index]);
        return entity;
    }, newline);

    const parsed = JSON.parse(migrated);
    validateLevel(parsed, path.basename(levelPath));
    return { content: migrated, buildingCount: spans.length, migratedCoreCount };
}

function validateProject(project) {
    const definitions = new Map(project.defs.entities.map(definition => [definition.identifier, definition]));
    for (const [identifier, expectedSize] of expectedSizes) {
        const definition = definitions.get(identifier);
        if (!definition) throw new Error(`Missing ${identifier} definition.`);
        if (definition.width !== expectedSize || definition.height !== expectedSize) {
            throw new Error(`${identifier} must be ${expectedSize}x${expectedSize}.`);
        }
        if (definition.renderMode !== "Rectangle" || definition.tilesetId !== null || definition.tileRect !== null) {
            throw new Error(`${identifier} must use native Rectangle rendering without a tileset.`);
        }
    }
    if (definitions.get("Building44").uid !== 51) throw new Error("Building44 UID changed unexpectedly.");
    if (definitions.get("Building55").uid !== 65) throw new Error("Building55 UID must be 65.");
    if (definitions.get("Building55").pivotSnapOffsetX || definitions.get("Building55").pivotSnapOffsetY) {
        throw new Error("Building55 must use normal grid-center snapping.");
    }
    if (project.defs.tilesets.some(tileset => previewIdentifiers.has(tileset.identifier))) {
        throw new Error("Building preview tilesets were not removed.");
    }
    if (project.nextUid !== 69) throw new Error(`City.ldtk nextUid must be 69, actual=${project.nextUid}.`);
}

function validateLevel(level, fileName) {
    for (const layer of level.layerInstances ?? []) {
        for (const entity of layer.entityInstances ?? []) {
            if (!buildingIdentifiers.has(entity.__identifier)) continue;
            if (entity.__tile !== null) throw new Error(`${fileName} ${entity.iid} still has a preview tile.`);
            const expectedSize = expectedSizes.get(entity.__identifier);
            if (entity.width !== expectedSize || entity.height !== expectedSize) {
                throw new Error(`${fileName} ${entity.iid} has invalid ${entity.__identifier} size.`);
            }
            if (entity.__identifier === "Building55") {
                if (entity.defUid !== 65) throw new Error(`${fileName} ${entity.iid} has invalid Building55 defUid.`);
                if (entity.px[0] % 16 !== 8 || entity.px[1] % 16 !== 8) {
                    throw new Error(`${fileName} ${entity.iid} Building55 pivot is not snapped to a grid center.`);
                }
            }
        }
    }
}

const migratedProject = migrateProject();
const migratedLevels = [];
let totalBuildings = 0;
let totalMigratedCores = 0;
for (const fileName of fs.readdirSync(levelRoot).filter(name => name.endsWith(".ldtkl")).sort()) {
    const levelPath = path.join(levelRoot, fileName);
    const result = migrateLevel(levelPath);
    migratedLevels.push({ levelPath, content: result.content });
    totalBuildings += result.buildingCount;
    totalMigratedCores += result.migratedCoreCount;
    console.log(`${fileName}: buildings=${result.buildingCount}, Building44->Building55=${result.migratedCoreCount}`);
}

fs.writeFileSync(projectPath, migratedProject, "utf8");
for (const level of migratedLevels) {
    fs.writeFileSync(level.levelPath, level.content, "utf8");
}
console.log(`Migration complete: buildings=${totalBuildings}, Building44->Building55=${totalMigratedCores}.`);
