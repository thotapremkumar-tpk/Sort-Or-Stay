/**
 * Paste this file into a new Google Apps Script project and run
 * createSortOrStayPlaytestForm once. The execution log has both editor links.
 * Review the draft in Google Forms and publish it when ready for responses.
 */
function createSortOrStayPlaytestForm() {
  const title = 'Sort or Stay | Playtest Survey';
  const form = FormApp.create(title, false);
  form.setDescription(
    'Tell the Sort or Stay project team about your play session. Your answers ' +
    'will help us evaluate the game and improve how it teaches sorting. ' +
    'Participation is voluntary. Please submit once after playing and leave ' +
    'names, email addresses, and student IDs out of your answers.'
  );
  form.setCollectEmail(false);
  form.setLimitOneResponsePerUser(false);
  form.setShuffleQuestions(false);
  form.setPublishingSummary(false);
  form.setProgressBar(true);
  form.setShowLinkToRespondAgain(false);
  form.setConfirmationMessage('Thanks for playing Sort or Stay and sharing your feedback.');

  const choose = (question, options) =>
    form.addMultipleChoiceItem().setTitle(question).setChoiceValues(options).setRequired(true);
  const section = (name, help) =>
    form.addPageBreakItem().setTitle(name).setHelpText(help);

  form.addSectionHeaderItem().setTitle('Your play session');
  choose('Before playing Sort or Stay, how familiar were you with sorting algorithms?', [
    'I had never heard of them', 'I had heard of them but could not explain one',
    'I could explain at least one', 'I had implemented at least one'
  ]);
  form.addCheckboxItem()
    .setTitle('Which floors did you try?')
    .setHelpText('Select every floor you tried, including floors you did not finish.')
    .setChoiceValues(['1 • Bubble sort', '2 • Selection sort', '3 • Insertion sort', '4 • Merge sort', '5 • Quick sort'])
    .setRequired(true);
  choose('How many floors did you finish?', ['0', '1', '2', '3', '4', '5', 'Not sure']);
  choose('Approximately how long did you play?', [
    'Under 10 minutes', '10–19 minutes', '20–29 minutes', '30–44 minutes', '45 minutes or more', 'Not sure'
  ]);
  choose('Which camera view did you use most?', [
    'First-person', 'Second-person', 'Third-person', 'I switched frequently', 'Not sure'
  ]);

  section('Sorting knowledge', 'Choose the best answer. Pick “Not sure” when you do not know.');
  // Answer key, in display order by question: 2, 1, 3, 2, 3.
  choose('Bubble sort is putting keys in ascending order. When should it swap two adjacent keys?', [
    'When the left key is smaller', 'When the left key is larger', 'At every comparison', 'Not sure'
  ]);
  choose('What does selection sort choose next when sorting crates from lightest to heaviest?', [
    'The lightest remaining crate', 'The heaviest remaining crate', 'The crate nearest the scale', 'Not sure'
  ]);
  choose('In insertion sort, which earlier books shift right to make room for the current book?', [
    'Books with smaller values', 'All books in the sorted section', 'Books with larger values', 'Not sure'
  ]);
  choose('When merging two ascending runs, which block goes into the output next?', [
    'The larger of the two front blocks', 'The smaller of the two front blocks', 'The last block of the longer run', 'Not sure'
  ]);
  choose('After an ascending quick-sort partition, where are values smaller than the pivot?', [
    'To the right of the pivot', 'On either side of the pivot', 'To the left of the pivot', 'Not sure'
  ]);
  choose('After playing, how confident are you that you could explain at least one sorting algorithm?', [
    'Not at all confident', 'Slightly confident', 'Moderately confident', 'Very confident', 'Extremely confident'
  ]);

  section('Gameplay feedback', 'Rate what you experienced during this session.');
  form.addGridItem()
    .setTitle('How much do you agree with each statement?')
    .setRows([
      'I could tell what I needed to do in each room.',
      'The monitor text was easy to read.',
      'It was easy to interact with puzzle objects.',
      'Professor Ada’s explanations were clear.',
      'The room quizzes helped me check my understanding.',
      'I enjoyed playing Sort or Stay.'
    ])
    .setColumns(['Strongly disagree', 'Disagree', 'Neutral', 'Agree', 'Strongly agree', 'Did not use or see'])
    .setRequired(true);
  form.addParagraphTextItem().setTitle('What was the most confusing part of the game?');
  form.addParagraphTextItem().setTitle('What is one change that would improve the game?');

  const sheet = SpreadsheetApp.create('Sort or Stay | Playtest Responses');
  form.setDestination(FormApp.DestinationType.SPREADSHEET, sheet.getId());
  console.log('Form editor: ' + form.getEditUrl());
  console.log('Response spreadsheet: ' + sheet.getUrl());
}
