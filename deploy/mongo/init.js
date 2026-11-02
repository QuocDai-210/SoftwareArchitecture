db = db.getSiblingDB("MatchDB");
db.createUser({ user: "matching", pwd: process.env.MATCH_DB_PASSWORD, roles: [{ role: "readWrite", db: "MatchDB" }] });
