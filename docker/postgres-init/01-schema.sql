-- ============================================================
--  Demo schema for the AI SQL Agent (education domain).
--  Mirrors a simplified M2SaaS-style model: students, teachers,
--  attendance, fees and leave. Runs automatically on first
--  container start (docker-entrypoint-initdb.d).
-- ============================================================

CREATE TABLE classes (
    id          SERIAL PRIMARY KEY,
    name        VARCHAR(50) NOT NULL,
    section     VARCHAR(10) NOT NULL
);

CREATE TABLE teachers (
    id          SERIAL PRIMARY KEY,
    name        VARCHAR(100) NOT NULL,
    subject     VARCHAR(50)  NOT NULL,
    salary      NUMERIC(10,2) NOT NULL,
    hired_on    DATE NOT NULL
);

CREATE TABLE students (
    id          SERIAL PRIMARY KEY,
    name        VARCHAR(100) NOT NULL,
    class_id    INT NOT NULL REFERENCES classes(id),
    gender      VARCHAR(10) NOT NULL,
    admitted_on DATE NOT NULL
);

CREATE TABLE attendance (
    id          SERIAL PRIMARY KEY,
    student_id  INT NOT NULL REFERENCES students(id),
    date        DATE NOT NULL,
    status      VARCHAR(10) NOT NULL      -- 'Present' | 'Absent' | 'Late'
);

CREATE TABLE fees (
    id          SERIAL PRIMARY KEY,
    student_id  INT NOT NULL REFERENCES students(id),
    month       VARCHAR(20) NOT NULL,
    amount      NUMERIC(10,2) NOT NULL,
    paid        BOOLEAN NOT NULL DEFAULT FALSE,
    paid_on     DATE
);

CREATE TABLE leaves (
    id          SERIAL PRIMARY KEY,
    teacher_id  INT NOT NULL REFERENCES teachers(id),
    from_date   DATE NOT NULL,
    to_date     DATE NOT NULL,
    reason      VARCHAR(200),
    approved    BOOLEAN NOT NULL DEFAULT FALSE
);
