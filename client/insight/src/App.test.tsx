import * as React from 'react';
// import { MemoryRouter } from 'react-router';
import { shallow } from 'enzyme';

import App from './App';

jest.mock('./data/store', () => {
  return {
    default: {
      dispatch: jest.fn()
    }
  };
});

jest.mock('./data/events', () => {
  return {
    requestLastWeek: () => 'requestLastWeek'
  };
});

it('renders a snapshot', () => {
  const val = shallow(<App/>);
  expect(val).toMatchSnapshot();
});
