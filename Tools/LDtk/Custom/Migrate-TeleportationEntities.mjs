import { randomUUID } from "node:crypto";
import { readFileSync, readdirSync, writeFileSync } from "node:fs";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const scriptDirectory = dirname(fileURLToPath(import.meta.url));
const repositoryRoot = join(scriptDirectory, "..", "..", "..");
const defaultProjectPath = join(repositoryRoot, "Assets", "AAAGame", "Tilemap", "Ldtk", "City.ldtk");
const arguments_ = process.argv.slice(2);
const resetLocalIds = arguments_.includes("--reset-local-ids");
const projectArguments = arguments_.filter(argument => argument !== "--reset-local-ids");
if (projectArguments.length > 1) {
    throw new Error("Usage: node Migrate-TeleportationEntities.mjs [project.ldtk] [--reset-local-ids]");
}
const projectPath = projectArguments.length === 0 ? defaultProjectPath : resolve(projectArguments[0]);
const levelDirectory = join(dirname(projectPath), "City");

const project = readJson(projectPath);
const entityDefinitions = project.defs?.entities;
if (!Array.isArray(entityDefinitions)) {
    throw new Error("City.ldtk has no entity definitions.");
}

const teleportationDefinition = requireSingle(
    entityDefinitions.filter(definition => definition.identifier === "Teleportation"),
    "Teleportation entity definition");
const defendDefinitions = entityDefinitions.filter(definition => definition.identifier === "DefendSpawn");
if (defendDefinitions.length > 1) {
    throw new Error(`DefendSpawn entity definition count must not exceed 1, actual=${defendDefinitions.length}.`);
}
const defendDefinition = defendDefinitions[0] ?? null;

const idFieldDefinition = teleportationDefinition.fieldDefs.find(field => field.identifier === "ID")
    ?? createIntegerFieldDefinition(project, "ID", 0, null);
const weightFieldDefinition = teleportationDefinition.fieldDefs.find(field => field.identifier === "Weight")
    ?? createFloatFieldDefinition(project, "Weight", 0, 0);
configureIntegerFieldDefinition(idFieldDefinition, 0, null);
configureFloatFieldDefinition(weightFieldDefinition, 0, 0);
teleportationDefinition.fieldDefs = [idFieldDefinition, weightFieldDefinition];
project.defs.entities = entityDefinitions.filter(definition => definition !== defendDefinition);

const levelFiles = readdirSync(levelDirectory)
    .filter(name => name.toLowerCase().endsWith(".ldtkl"))
    .sort((left, right) => left.localeCompare(right, "en"));
const levelsByName = new Map();
for (const fileName of levelFiles) {
    levelsByName.set(fileName, readJson(join(levelDirectory, fileName)));
}

for (const fileName of levelFiles) {
    const level = levelsByName.get(fileName);
    const entityLayer = requireLayer(level, "Entities");
    const defendInstances = entityLayer.entityInstances.filter(instance => instance.__identifier === "DefendSpawn");
    let teleportationInstances = entityLayer.entityInstances.filter(instance => instance.__identifier === "Teleportation");

    if (defendInstances.length > 0 && teleportationInstances.length === 0 && fileName.includes("_Backup")) {
        const sourceName = fileName.replace("_Backup", "");
        const sourceLevel = levelsByName.get(sourceName);
        if (sourceLevel == null) {
            throw new Error(`${fileName} requires source level ${sourceName} to restore teleportation points.`);
        }
        const sourceEntities = requireLayer(sourceLevel, "Entities").entityInstances
            .filter(instance => instance.__identifier === "Teleportation");
        if (sourceEntities.length === 0) {
            throw new Error(`${sourceName} has no teleportation points to copy into ${fileName}.`);
        }
        for (const source of sourceEntities) {
            const clone = structuredClone(source);
            clone.iid = randomUUID();
            clone.fieldInstances = [];
            clone.__worldX = (level.worldX ?? 0) + clone.px[0];
            clone.__worldY = (level.worldY ?? 0) + clone.px[1];
            entityLayer.entityInstances.push(clone);
        }
        teleportationInstances = entityLayer.entityInstances.filter(instance => instance.__identifier === "Teleportation");
    }

    if (defendInstances.length === 0 && teleportationInstances.length === 0) {
        continue;
    }

    const strongholdLayer = requireLayer(level, "SH");
    const components = buildStrongholdComponents(strongholdLayer);
    const defendByComponent = new Map();
    for (const instance of defendInstances) {
        const component = requireComponent(instance, strongholdLayer, components, fileName);
        if (component.factionId !== 1) {
            throw new Error(`${fileName} DefendSpawn ${instance.iid} is not in an enemy stronghold.`);
        }
        if (defendByComponent.has(component.id)) {
            throw new Error(`${fileName} has multiple DefendSpawn entities in SH component ${component.id}.`);
        }
        defendByComponent.set(component.id, readNumericField(instance, "Weight"));
    }

    const teleportationComponents = new Set();
    for (const instance of teleportationInstances) {
        const component = requireComponent(instance, strongholdLayer, components, fileName);
        if (teleportationComponents.has(component.id)) {
            throw new Error(`${fileName} has multiple Teleportation entities in SH component ${component.id}.`);
        }
        teleportationComponents.add(component.id);

        let weight;
        if (component.factionId === 2) {
            weight = 0;
        } else if (component.factionId === 1) {
            if (defendByComponent.has(component.id)) {
                weight = defendByComponent.get(component.id);
            } else {
                weight = readNumericField(instance, "Weight");
            }
        } else {
            throw new Error(`${fileName} Teleportation ${instance.iid} has unsupported SH faction ${component.factionId}.`);
        }

        instance.__identifier = "Teleportation";
        instance.__smartColor = teleportationDefinition.color;
        instance.defUid = teleportationDefinition.uid;
        instance.fieldInstances = [
            readExistingField(instance, "ID"),
            createFloatFieldInstance(weightFieldDefinition, weight),
        ].filter(value => value != null);
        instance.__teleportationWeight = weight;
    }

    if (defendByComponent.size !== defendInstances.length) {
        throw new Error(`${fileName} failed to map every DefendSpawn to a stronghold component.`);
    }
    for (const componentId of defendByComponent.keys()) {
        if (!teleportationComponents.has(componentId)) {
            throw new Error(`${fileName} DefendSpawn component ${componentId} has no matching Teleportation.`);
        }
    }

    entityLayer.entityInstances = entityLayer.entityInstances.filter(instance => instance.__identifier !== "DefendSpawn");
}

const nextIds = [];
for (const fileName of levelFiles) {
    const instances = requireLayer(levelsByName.get(fileName), "Entities").entityInstances
        .filter(instance => instance.__identifier === "Teleportation");
    let maxId = -1;

    if (resetLocalIds) {
        const ordered = instances.map(instance => {
            const field = readExistingField(instance, "ID");
            const id = field == null ? null : field.__value;
            if (id != null && (!Number.isInteger(id) || id < 0)) {
                throw new Error(`${fileName} Teleportation ${instance.iid} has invalid ID ${id}.`);
            }
            return { instance, id };
        }).sort((left, right) => {
            if (left.id == null && right.id != null) return 1;
            if (left.id != null && right.id == null) return -1;
            if (left.id !== right.id) return (left.id ?? 0) - (right.id ?? 0);
            return left.instance.iid.localeCompare(right.instance.iid, "en");
        });
        for (let index = 0; index < ordered.length; index++) {
            ordered[index].instance.__teleportationId = index;
            maxId = index;
        }
    } else {
        const usedIds = new Map();
        for (const instance of instances) {
            const field = readExistingField(instance, "ID");
            if (field == null) {
                continue;
            }
            const id = field.__value;
            if (!Number.isInteger(id) || id < 0) {
                throw new Error(`${fileName} Teleportation ${instance.iid} has invalid ID ${id}.`);
            }
            if (usedIds.has(id)) {
                throw new Error(`${fileName} has duplicate Teleportation ID ${id}: ${usedIds.get(id)} and ${instance.iid}.`);
            }
            usedIds.set(id, instance.iid);
            instance.__teleportationId = id;
            maxId = Math.max(maxId, id);
        }
        for (const instance of instances) {
            if (instance.__teleportationId == null) {
                if (maxId >= 2147483647) {
                    throw new Error(`${fileName} Teleportation ID range is exhausted.`);
                }
                instance.__teleportationId = ++maxId;
            }
        }
    }

    for (const instance of instances) {
        const id = instance.__teleportationId;
        const weight = instance.__teleportationWeight;
        delete instance.__teleportationId;
        delete instance.__teleportationWeight;
        instance.fieldInstances = [
            createIntegerFieldInstance(idFieldDefinition, id),
            createFloatFieldInstance(weightFieldDefinition, weight),
        ];
    }
    nextIds.push(`${fileName}=${maxId + 1}`);
}

writeJson(projectPath, project);
for (const fileName of levelFiles) {
    writeJson(join(levelDirectory, fileName), levelsByName.get(fileName));
}

let teleportationCount = 0;
forEachTeleportation(levelFiles, levelsByName, () => teleportationCount++);
console.log(`Migrated ${teleportationCount} Teleportation entities; next IDs: ${nextIds.join(", ")}.`);

function readJson(path) {
    return JSON.parse(readFileSync(path, "utf8"));
}

function writeJson(path, value) {
    const json = `${JSON.stringify(value, null, "\t")}\n`.replace(/\n/g, "\r\n");
    writeFileSync(path, json, "utf8");
}

function requireSingle(values, label) {
    if (values.length !== 1) {
        throw new Error(`${label} count must be 1, actual=${values.length}.`);
    }
    return values[0];
}

function requireLayer(level, identifier) {
    return requireSingle(
        (level.layerInstances ?? []).filter(layer => layer.__identifier === identifier),
        `${level.identifier ?? "<unnamed>"} ${identifier} layer`);
}

function createIntegerFieldDefinition(sourceProject, identifier, minimum, defaultValue) {
    const uid = sourceProject.nextUid++;
    const definition = {
        identifier,
        doc: null,
        uid,
        isArray: false,
        canBeNull: false,
        arrayMinLength: null,
        arrayMaxLength: null,
        editorDisplayMode: "NameAndValue",
        editorDisplayScale: 1,
        editorDisplayPos: "Above",
        editorLinkStyle: "StraightArrow",
        editorDisplayColor: null,
        editorAlwaysShow: true,
        editorShowInWorld: true,
        editorCutLongValues: true,
        editorTextSuffix: null,
        editorTextPrefix: null,
        useForSmartColor: false,
        exportToToc: false,
        searchable: false,
        max: null,
        regex: null,
        acceptFileTypes: null,
        textLanguageMode: null,
        symmetricalRef: false,
        autoChainRef: true,
        allowOutOfLevelRef: true,
        allowedRefs: "OnlySame",
        allowedRefsEntityUid: null,
        allowedRefTags: [],
        tilesetUid: null,
    };
    configureIntegerFieldDefinition(definition, minimum, defaultValue);
    return definition;
}

function createFloatFieldDefinition(sourceProject, identifier, minimum, defaultValue) {
    const definition = createIntegerFieldDefinition(sourceProject, identifier, minimum, defaultValue);
    configureFloatFieldDefinition(definition, minimum, defaultValue);
    return definition;
}

function configureIntegerFieldDefinition(definition, minimum, defaultValue) {
    definition.__type = "Int";
    definition.type = "F_Int";
    definition.min = minimum;
    definition.defaultOverride = defaultValue == null ? null : { id: "V_Int", params: [defaultValue] };
}

function configureFloatFieldDefinition(definition, minimum, defaultValue) {
    definition.__type = "Float";
    definition.type = "F_Float";
    definition.min = minimum;
    definition.defaultOverride = defaultValue == null ? null : { id: "V_Float", params: [defaultValue] };
}

function createIntegerFieldInstance(definition, value) {
    if (!Number.isInteger(value) || value < definition.min) {
        throw new Error(`${definition.identifier} must be an integer >= ${definition.min}, actual=${value}.`);
    }
    return {
        __identifier: definition.identifier,
        __type: "Int",
        __value: value,
        __tile: null,
        defUid: definition.uid,
        realEditorValues: [{ id: "V_Int", params: [value] }],
    };
}

function createFloatFieldInstance(definition, value) {
    if (!Number.isFinite(value) || value < definition.min) {
        throw new Error(`${definition.identifier} must be a finite number >= ${definition.min}, actual=${value}.`);
    }
    return {
        __identifier: definition.identifier,
        __type: "Float",
        __value: value,
        __tile: null,
        defUid: definition.uid,
        realEditorValues: [{ id: "V_Float", params: [value] }],
    };
}

function readExistingField(instance, identifier) {
    return (instance.fieldInstances ?? []).find(field => field.__identifier === identifier) ?? null;
}

function readNumericField(instance, identifier) {
    const field = readExistingField(instance, identifier);
    if (field == null || !Number.isFinite(field.__value)) {
        throw new Error(`${instance.__identifier} ${instance.iid} requires numeric field ${identifier}.`);
    }
    const value = field.__value;
    if (value < 0) {
        throw new Error(`${instance.__identifier} ${instance.iid} has invalid ${identifier}=${value}.`);
    }
    return value;
}

function buildStrongholdComponents(layer) {
    const width = layer.__cWid;
    const height = layer.__cHei;
    const cells = layer.intGridCsv;
    if (!Number.isInteger(width) || !Number.isInteger(height) || cells?.length !== width * height) {
        throw new Error("SH layer grid is invalid.");
    }
    const components = new Array(cells.length).fill(null);
    let nextId = 0;
    for (let index = 0; index < cells.length; index++) {
        if (cells[index] === 0 || components[index] != null) {
            continue;
        }
        const component = { id: ++nextId, factionId: cells[index] };
        const queue = [index];
        components[index] = component;
        for (let cursor = 0; cursor < queue.length; cursor++) {
            const current = queue[cursor];
            const x = current % width;
            const y = Math.floor(current / width);
            const neighbours = [
                x > 0 ? current - 1 : -1,
                x + 1 < width ? current + 1 : -1,
                y > 0 ? current - width : -1,
                y + 1 < height ? current + width : -1,
            ];
            for (const neighbour of neighbours) {
                if (neighbour >= 0 && components[neighbour] == null && cells[neighbour] === component.factionId) {
                    components[neighbour] = component;
                    queue.push(neighbour);
                }
            }
        }
    }
    return components;
}

function requireComponent(instance, layer, components, fileName) {
    const gridSize = layer.__gridSize;
    const x = Math.floor(instance.px[0] / gridSize);
    const y = Math.floor(instance.px[1] / gridSize);
    const component = components[x + y * layer.__cWid];
    if (component == null) {
        throw new Error(`${fileName} ${instance.__identifier} ${instance.iid} is outside every SH component.`);
    }
    return component;
}

function forEachTeleportation(fileNames, levelMap, callback) {
    for (const fileName of fileNames) {
        const entityLayer = requireLayer(levelMap.get(fileName), "Entities");
        for (const instance of entityLayer.entityInstances) {
            if (instance.__identifier === "Teleportation") {
                callback(instance, fileName);
            }
        }
    }
}
