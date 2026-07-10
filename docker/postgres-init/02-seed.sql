-- ============================================================
--  Sample data so the agent has something meaningful to answer.
--  Intentionally UNEVEN across classes/genders/months so that
--  aggregate queries produce varied, chart-worthy results.
-- ============================================================

INSERT INTO classes (name, section) VALUES
    ('Grade 6', 'A'), ('Grade 7', 'A'), ('Grade 8', 'B'),
    ('Grade 9', 'A'), ('Grade 10', 'B');

INSERT INTO teachers (name, subject, salary, hired_on) VALUES
    ('Rahim Uddin',    'Mathematics', 45000, '2019-01-15'),
    ('Karim Ahmed',    'Physics',     52000, '2018-06-01'),
    ('Fatima Begum',   'English',     48000, '2020-03-10'),
    ('Nasrin Akter',   'Biology',     41000, '2021-08-20'),
    ('Jamal Hossain',  'Chemistry',   55000, '2017-02-28'),
    ('Shirin Sultana', 'Bangla',      39000, '2022-05-11'),
    ('Abdul Malek',    'ICT',         47000, '2019-11-03'),
    ('Rokeya Khatun',  'History',     43000, '2020-09-19');

-- Uneven students per class (Grade 6 has many, Grade 10 has few),
-- and an uneven gender split overall.
INSERT INTO students (name, class_id, gender, admitted_on) VALUES
    -- Grade 6 (7 students)
    ('Ayesha Siddika', 1, 'Female', '2024-01-05'),
    ('Tanvir Islam',   1, 'Male',   '2024-01-06'),
    ('Sadia Rahman',   1, 'Female', '2024-01-07'),
    ('Rakib Hasan',    1, 'Male',   '2024-01-08'),
    ('Nusrat Jahan',   1, 'Female', '2024-01-09'),
    ('Imran Kabir',    1, 'Male',   '2024-01-10'),
    ('Mitu Akter',     1, 'Female', '2024-01-11'),
    -- Grade 7 (5 students)
    ('Sabbir Ahmed',   2, 'Male',   '2023-01-12'),
    ('Farhana Yasmin', 2, 'Female', '2023-01-13'),
    ('Habib Rahman',   2, 'Male',   '2023-01-14'),
    ('Sumaiya Islam',  2, 'Female', '2023-01-15'),
    ('Arif Hossain',   2, 'Male',   '2023-01-16'),
    -- Grade 8 (4 students)
    ('Jannat Ara',     3, 'Female', '2022-01-17'),
    ('Shakil Khan',    3, 'Male',   '2022-01-18'),
    ('Mim Chowdhury',  3, 'Female', '2022-01-19'),
    ('Rasel Mia',      3, 'Male',   '2022-01-20'),
    -- Grade 9 (3 students)
    ('Tasnia Haque',   4, 'Female', '2021-01-21'),
    ('Nayeem Hasan',   4, 'Male',   '2021-01-22'),
    ('Priya Das',      4, 'Female', '2021-01-23'),
    -- Grade 10 (2 students)
    ('Sabbir Alam',    5, 'Male',   '2020-01-24'),
    ('Lamia Akter',    5, 'Female', '2020-01-25');

-- Attendance across a few days (varied present/absent/late per student).
INSERT INTO attendance (student_id, date, status)
SELECT s.id, d.date,
    CASE
        WHEN (s.id + EXTRACT(DAY FROM d.date)::int) % 5 = 0 THEN 'Absent'
        WHEN (s.id + EXTRACT(DAY FROM d.date)::int) % 7 = 0 THEN 'Late'
        ELSE 'Present'
    END
FROM students s
CROSS JOIN (VALUES
    (DATE '2026-07-01'), (DATE '2026-07-02'), (DATE '2026-07-03'),
    (DATE '2026-07-04'), (DATE '2026-07-05')
) AS d(date);

-- Fees for July with varied amounts and paid/unpaid mix.
INSERT INTO fees (student_id, month, amount, paid, paid_on)
SELECT s.id, 'July',
    3000 + (s.class_id * 500),               -- higher grades pay more
    (s.id % 3 <> 0),                          -- ~1/3 unpaid
    CASE WHEN (s.id % 3 <> 0) THEN DATE '2026-07-03' ELSE NULL END
FROM students s;

-- A few June fees too, so month-over-month queries have data.
INSERT INTO fees (student_id, month, amount, paid, paid_on)
SELECT s.id, 'June', 3000 + (s.class_id * 500), TRUE, DATE '2026-06-04'
FROM students s WHERE s.class_id <= 3;

INSERT INTO leaves (teacher_id, from_date, to_date, reason, approved) VALUES
    (1, '2026-07-05', '2026-07-06', 'Family event', TRUE),
    (2, '2026-07-10', '2026-07-12', 'Medical',      FALSE),
    (3, '2026-07-08', '2026-07-08', 'Personal',     TRUE),
    (4, '2026-07-15', '2026-07-16', 'Conference',   FALSE),
    (5, '2026-07-20', '2026-07-21', 'Family event', TRUE),
    (7, '2026-07-22', '2026-07-23', 'Medical',      FALSE);
