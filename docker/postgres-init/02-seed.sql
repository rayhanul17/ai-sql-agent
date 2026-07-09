-- ============================================================
--  Sample data so the agent has something meaningful to answer.
-- ============================================================

INSERT INTO classes (name, section) VALUES
    ('Grade 6', 'A'), ('Grade 7', 'A'), ('Grade 8', 'B'), ('Grade 9', 'A');

INSERT INTO teachers (name, subject, salary, hired_on) VALUES
    ('Rahim Uddin',    'Mathematics', 45000, '2019-01-15'),
    ('Karim Ahmed',    'Physics',     52000, '2018-06-01'),
    ('Fatima Begum',   'English',     48000, '2020-03-10'),
    ('Nasrin Akter',   'Biology',     41000, '2021-08-20'),
    ('Jamal Hossain',  'Chemistry',   55000, '2017-02-28');

INSERT INTO students (name, class_id, gender, admitted_on) VALUES
    ('Ayesha Siddika', 1, 'Female', '2024-01-05'),
    ('Tanvir Islam',   1, 'Male',   '2024-01-06'),
    ('Sadia Rahman',   2, 'Female', '2023-01-10'),
    ('Rakib Hasan',    2, 'Male',   '2023-01-11'),
    ('Nusrat Jahan',   3, 'Female', '2022-01-12'),
    ('Imran Kabir',    3, 'Male',   '2022-01-13'),
    ('Mitu Akter',     4, 'Female', '2021-01-14'),
    ('Sabbir Ahmed',   4, 'Male',   '2021-01-15');

-- Attendance for the current month (mix of present/absent/late).
INSERT INTO attendance (student_id, date, status) VALUES
    (1, '2026-07-01', 'Present'), (2, '2026-07-01', 'Absent'),
    (3, '2026-07-01', 'Present'), (4, '2026-07-01', 'Late'),
    (5, '2026-07-01', 'Absent'),  (6, '2026-07-01', 'Present'),
    (7, '2026-07-01', 'Present'), (8, '2026-07-01', 'Absent'),
    (1, '2026-07-02', 'Present'), (2, '2026-07-02', 'Absent'),
    (3, '2026-07-02', 'Absent'),  (4, '2026-07-02', 'Present'),
    (5, '2026-07-02', 'Present'), (6, '2026-07-02', 'Late'),
    (7, '2026-07-02', 'Absent'),  (8, '2026-07-02', 'Present');

-- Fees for July: some paid, some due.
INSERT INTO fees (student_id, month, amount, paid, paid_on) VALUES
    (1, 'July', 3000, TRUE,  '2026-07-03'),
    (2, 'July', 3000, FALSE, NULL),
    (3, 'July', 3500, TRUE,  '2026-07-02'),
    (4, 'July', 3500, FALSE, NULL),
    (5, 'July', 4000, TRUE,  '2026-07-04'),
    (6, 'July', 4000, FALSE, NULL),
    (7, 'July', 4500, TRUE,  '2026-07-01'),
    (8, 'July', 4500, FALSE, NULL);

INSERT INTO leaves (teacher_id, from_date, to_date, reason, approved) VALUES
    (1, '2026-07-05', '2026-07-06', 'Family event',     TRUE),
    (2, '2026-07-10', '2026-07-12', 'Medical',          FALSE),
    (3, '2026-07-08', '2026-07-08', 'Personal',         TRUE),
    (4, '2026-07-15', '2026-07-16', 'Conference',       FALSE);
